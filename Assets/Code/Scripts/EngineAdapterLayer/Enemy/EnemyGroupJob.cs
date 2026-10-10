using Kizami.BlackBoard;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループを 1 つずつ更新する。待機・追跡・帰還の切り替え、進む・待つの切り替え、アンカーの移動と道筋・帰りの道筋の記録、隊列の 1 列の数、包囲の置き場の割り当てを行う。
    /// 持ち場が追跡範囲（距離マップを計算した範囲）に入れば追跡に、外れれば帰還にする。範囲が変わるのは区画の切り替えのときだけなので、毎フレーム調べても切り替えのときだけ変わる。
    /// 別のグループや別の敵の置き場を見るので、順番に依存しないよう、並列にせず 1 つの Job で回す。
    /// </summary>
    [BurstCompile]
    public struct EnemyGroupJob : IJob
    {
        /// <summary> アンカーが止まっているとみなす速さ（m/s）。帰還中にこれより遅い間を、進めないとする </summary>
        private const float STOPPED_SPEED = 0.5f;

        /// <summary> アンカーが向かう先として、距離マップの値が下がる列をたどる数 </summary>
        private const int LOOKAHEAD_STEPS = 6;

        /// <summary> 1 列に並べる数を決めるときに、床の幅を調べる進む先の距離（m）。細い所の手前で組み替えを済ませる </summary>
        private const float COLUMN_LOOKAHEAD_DISTANCE = 18f;

        /// <summary> 進む先の床の幅を調べる間隔（m） </summary>
        private const float COLUMN_LOOKAHEAD_INTERVAL = 3f;

        /// <summary> 置き場が立てる層のない列に来たときに、ずらす先の列を探す半径（m） </summary>
        private const float SLOT_SHIFT_RADIUS = 10f;

        /// <summary> アンカーが包囲の置き場にこの距離（m）まで近づいたら、着いたとみなす </summary>
        private const float ANCHOR_ARRIVE_DISTANCE = 1f;

        /// <summary> アンカーが包囲の置き場までの距離がこの値（m）より近いと、近さに合わせて遅くなる </summary>
        private const float ANCHOR_SLOW_DOWN_DISTANCE = 4f;

        /// <summary> 帰還中のアンカーが帰りの道筋の点にこの距離（m）まで近づいたら、その点を捨てて次の点へ向かう </summary>
        private const float RETURN_POINT_REACH_DISTANCE = 2f;

        /// <summary> 待機を始めてから処理を止めるまでの、メンバーが隊列に並び終える時間に足す余裕（秒） </summary>
        private const float DORMANT_MARGIN = 1f;


        public NativeArray<EnemyGroup> Groups;

        /// <summary> 置き場ごとの、立てる列へずらした位置。使えない置き場は NaN。毎フレーム書き直す </summary>
        public NativeArray<float2> SlotPoints;

        /// <summary> 0 番に、使える置き場のうち最も外の置き場の番号を書く。これより外の置き場は待つ置き場。なければ -1 </summary>
        public NativeArray<int> LastUsableSlot;

        /// <summary> グループごとに EnemyGroups.PATH_CAPACITY 個の区画を持つ道筋の点 </summary>
        public NativeArray<float3> Paths;

        /// <summary> グループごとに EnemyGroups.RETURN_PATH_CAPACITY 個の区画を持つ帰りの道筋の点 </summary>
        public NativeArray<float3> ReturnPaths;

        /// <summary> グループごとに EnemyFormationSettings.MAX_GROUP_SIZE 個の区画を持つ、メンバーの敵の番号 </summary>
        [ReadOnly] public NativeArray<int> Members;

        [ReadOnly] public NativeArray<EnemyAgent> Agents;
        public EnemyNavigationGrid Grid;
        [ReadOnly] public NativeArray<float> Distances;

        /// <summary> Distances を計算した追跡範囲の、最小の列 (x, z) </summary>
        public int2 TrackingMin;

        /// <summary> Distances を計算した追跡範囲の、最大の列 (x, z)。この列も含む </summary>
        public int2 TrackingMax;

        public EnemyFormationSettings Formation;

        /// <summary> プレイヤーの位置。グループの置き場の螺旋の中心 </summary>
        public float3 PlayerPosition;

        /// <summary> 敵が歩く速さ（m/s） </summary>
        public float MoveSpeed;

        public float DeltaTime;

        private static float MoveTowards(float current, float target, float maxDelta)
        {
            return current < target ? math.min(current + maxDelta, target) : math.max(current - maxDelta, target);
        }

        /// <summary>
        /// 飛んでいるか、落ちている生きたメンバーがいるか。
        /// </summary>
        private bool HasAirborneMember(int g, in EnemyGroup group)
        {
            for (var i = 0; i < group.MemberCount; i++)
            {
                var agent = Agents[Members[g * EnemyFormationSettings.MAX_GROUP_SIZE + i]];
                if (agent.IsAlive && agent.GroupIndex == g && (!agent.IsGrounded || agent.MoveMode == EnemyMoveMode.Flying)) return true;
            }

            return false;
        }

        private bool HasAliveMember(int g, in EnemyGroup group)
        {
            for (var i = 0; i < group.MemberCount; i++)
            {
                var agent = Agents[Members[g * EnemyFormationSettings.MAX_GROUP_SIZE + i]];
                if (agent.IsAlive && agent.GroupIndex == g) return true;
            }

            return false;
        }

        /// <summary>
        /// 追跡範囲の外で待機しているグループの処理を止めるかを決める（IsDormant）。止めたグループと、そのメンバーのうち立って歩く敵は、EnemyGroupJob・EnemyMoveJob で動かさず、その場に描くだけにする。
        /// 待機を始めてから、隊列の長さを歩く時間 ＋ DORMANT_MARGIN が経つまでは止めない。持ち場に着いた直後や生成の直後は、メンバーがまだ隊列へ歩いている為。
        /// 飛んでいる・落ちているメンバーがいる間は、数えた時間を 0 に戻す。着地してから隊列へ歩く時間を残す為。
        /// 止めている間は足場が壊れても落ちず、範囲に入って処理が戻ったときに落ちる。
        /// </summary>
        private void UpdateDormant(int g, ref EnemyGroup group)
        {
            if (group.State != EnemyGroupState.Waiting || IsInTrackingRange(group.AnchorPosition)
                || HasAirborneMember(g, group))
            {
                group.WaitingTime = 0f;
                group.IsDormant = false;
                return;
            }

            group.WaitingTime += DeltaTime;
            var settleTime = MoveSpeed > 0f ? GetFormationLength(group) / MoveSpeed : 0f;
            group.IsDormant = group.WaitingTime >= settleTime + DORMANT_MARGIN;
        }

        /// <summary>
        /// 位置の真下の列が、距離マップを計算した追跡範囲の中にあるか。格子の外は範囲の外とする。
        /// </summary>
        private bool IsInTrackingRange(float3 position)
        {
            if (!Grid.TryGetColumn(position, out var column)) return false;

            var x = column % Grid.Width;
            var z = column / Grid.Width;
            return x >= TrackingMin.x && x <= TrackingMax.x && z >= TrackingMin.y && z <= TrackingMax.y;
        }

        /// <summary>
        /// 今の状態を命じられた状態（CommandedState）に切り替える。
        /// 追跡を始めるときは、帰りの道筋が空なら持ち場を最初の点にする。
        /// どちらも、アンカーが向かう先と逆を向いていれば、隊列を前後に入れ替える。待機に入るときは帰りの道筋を捨てる。帰還の観測（着いたか、進めないか）は切り替えるたびに消す。
        /// </summary>
        private void ApplyCommand(int g, ref EnemyGroup group)
        {
            if (group.State == group.CommandedState) return;

            group.State = group.CommandedState;
            group.HasReachedHome = false;
            group.IsReturnBlocked = false;
            switch (group.State)
            {
                case EnemyGroupState.Tracking:
                    if (group.ReturnCount == 0) PushReturnPoint(g, ref group, group.HomePosition);

                    FaceFormation(g, ref group, (PlayerPosition - group.AnchorPosition).xz);
                    break;
                case EnemyGroupState.Returning:
                    group.HasArrived = false;
                    FaceFormation(g, ref group, (GetReturnTarget(g, group) - group.AnchorPosition).xz);
                    break;
                default:
                    group.ReturnCount = 0;
                    group.AnchorSpeed = 0f;
                    break;
            }
        }

        /// <summary>
        /// アンカーの向きが direction と逆（内積が負）なら、仮想のアンカーを隊列の最後尾へ移し、道筋を前後逆に作り直して、隊列を前後に入れ替える。
        /// 道筋は後ろへたどって隊列を並べるので、そのまま向きを変えると、隊列の位置がアンカーの前へ折り返す為。
        /// メンバーの順番は、EnemyGroups.MaintainNext の並べ替えで向かう先に近い順になる。
        /// </summary>
        private void FaceFormation(int g, ref EnemyGroup group, float2 direction)
        {
            var forward = new float2(math.sin(group.AnchorYaw), math.cos(group.AnchorYaw));
            if (math.dot(forward, direction) >= 0f) return;

            var length = GetFormationLength(group);
            var count = math.min(EnemyGroups.PATH_CAPACITY, (int)math.ceil(length / EnemyGroups.PATH_SPACING) + 1);
            var samples = new NativeArray<float3>(count, Allocator.Temp);
            for (var i = 0; i < count; i++)
            {
                EnemyGroups.SamplePath(Paths, g, group, math.min(i * EnemyGroups.PATH_SPACING, length), out var point, out _);
                samples[i] = point;
            }

            // 元のアンカーの位置を最も古い点に、元の最後尾を最も新しい点（新しいアンカー）にする
            var offset = g * EnemyGroups.PATH_CAPACITY;
            for (var i = 0; i < count; i++) Paths[offset + i] = samples[i];

            group.PathHead = count - 1;
            group.PathCount = count;
            group.AnchorPosition = samples[count - 1];
            group.AnchorSpeed = 0f;
            group.AnchorYaw = count > 1
                ? math.atan2(samples[count - 1].x - samples[count - 2].x, samples[count - 1].z - samples[count - 2].z)
                : group.AnchorYaw + math.PI;
            samples.Dispose();
        }

        /// <summary>
        /// アンカーを床の高さへ合わせ、立っている列とノード、そのノードのプレイヤーまでの経路の長さを書く。乗れる層がなければ止めて false。
        /// </summary>
        private bool TryFitToFloor(ref EnemyGroup group, out int column, out int node)
        {
            node = -1;
            if (!Grid.TryGetColumn(group.AnchorPosition, out column))
            {
                group.AnchorSpeed = 0f;
                return false;
            }

            node = Grid.GetHighestNodeBelow(column, group.AnchorPosition.y + Grid.ClimbHeight);
            if (node < 0)
            {
                group.AnchorSpeed = 0f;
                return false;
            }

            group.AnchorPosition.y = Grid.Heights[node];
            group.AnchorDistance = Distances[node];
            return true;
        }

        /// <summary>
        /// 追跡中のアンカーを、Application が割り当てた目標位置（包囲の置き場。使える置き場が空いていなければ、その外に続く待つ置き場）へ歩かせる。
        /// 割り当てられた置き場がないか、このフレームで使えなくなった（NaN）ときは、最も外の使える置き場のさらに 1 周外で待つ。
        /// プレイヤーまでの経路が「目標位置のプレイヤーまでの経路 ＋ 近づく余裕」より長い間は距離マップの値が下がる方へ、内側では目標位置へ向かう。
        /// 目標位置へまっすぐ向かうと、壁の向こうの目標位置の手前で詰まる為。経路の長さで比べるので、壁を回り込んでから目標位置へ向かう。
        /// 置き場に着いたら止まってプレイヤーを向き、メンバーはグループの中心の周りの螺旋に並ぶ。置き場が FollowDistance 動くまでは、並んだままついていく。
        /// バリアを張っている間は、プレイヤーとの距離が張ったときの距離 ＋ BarrierLeaveMargin を超えるまで、置き場が動いてもついていかずに留まる。
        /// 距離マップを下る間は、たどり着けないときと、同じレーンの前で別のグループが待つ番で止まっているときに、止まるまで減速する。
        /// </summary>
        private void MoveAnchor(int g, ref EnemyGroup group)
        {
            var lastUsableSlot = LastUsableSlot[0];
            if (!TryFitToFloor(ref group, out var column, out var node)) return;

            var height = Grid.Heights[node];
            var distance = group.AnchorDistance;
            var hasSlot = group.EncircleSlot >= 0 && !math.any(math.isnan(SlotPoints[group.EncircleSlot]));
            var isWaitingSlot = group.EncircleSlot > lastUsableSlot;
            float2 target;
            if (hasSlot)
            {
                target = SlotPoints[group.EncircleSlot];
            }
            else
            {
                // 置き場がすべて埋まっているときは、最も外の使える置き場のさらに 1 周外で、来た向きに待つ
                var outermostRadius = lastUsableSlot >= 0 ? GetEncircleRadius(lastUsableSlot) : Formation.EncircleInnerRadius;
                var fromPlayer = math.normalizesafe(group.AnchorPosition.xz - PlayerPosition.xz, new float2(0f, 1f));
                target = PlayerPosition.xz + fromPlayer * (outermostRadius + Formation.EncircleLoopSpacing);
            }

            var targetPathDistance = GetPathDistance(target);
            var targetSpeed = 0f;
            var hasHeading = false;
            var heading = float2.zero;
            var toTarget = target - group.AnchorPosition.xz;
            var targetDistance = math.length(toTarget);

            // バリアを張っているグループは、プレイヤーが近づく分には置き場が動いても位置に留まり、プレイヤーを向く
            var isGuarding = group.HasArrived && group.IsBarrierRaised;
            if (isGuarding && math.distance(PlayerPosition.xz, group.AnchorPosition.xz)
                <= group.BarrierStayDistance + Formation.BarrierLeaveMargin)
            {
                Advance(ref group, column, node, true, (PlayerPosition - group.AnchorPosition).xz, 0f);
                return;
            }

            // 着いたグループは、置き場が FollowDistance 動くまでは螺旋に並んだまま、置き場へついていく
            if (group.HasArrived && !isGuarding && hasSlot && !isWaitingSlot && targetDistance < Formation.FollowDistance)
            {
                if (targetDistance > ANCHOR_ARRIVE_DISTANCE
                    && TryGetHeadingToward(group.AnchorPosition, column, height, distance, toTarget, out heading))
                {
                    hasHeading = true;
                    targetSpeed = MoveSpeed * Formation.AnchorSpeedRate * math.saturate(targetDistance / ANCHOR_SLOW_DOWN_DISTANCE);
                }

                Advance(ref group, column, node, hasHeading, heading, targetSpeed);
                return;
            }

            if (group.HasArrived) LeaveSpiral(g, ref group, toTarget);

            if (distance > targetPathDistance + Formation.EncircleApproachMargin)
            {
                var nextColumn = -1;
                hasHeading = !float.IsPositiveInfinity(distance)
                             && Grid.TryGetDownhillColumn(column, height, distance, Distances, out nextColumn);
                if (hasHeading)
                {
                    heading = GetDownhillHeading(group.AnchorPosition, column, height, nextColumn);
                    targetSpeed = MoveSpeed * Formation.AnchorSpeedRate;
                }
            }
            else
            {
                if (targetDistance <= ANCHOR_ARRIVE_DISTANCE)
                {
                    group.HasArrived = hasSlot && !isWaitingSlot;
                    heading = (PlayerPosition - group.AnchorPosition).xz;
                    hasHeading = true;
                }
                else if (TryGetHeadingToward(group.AnchorPosition, column, height, distance, toTarget, out heading))
                {
                    hasHeading = true;
                    targetSpeed = MoveSpeed * Formation.AnchorSpeedRate
                                  * math.saturate(targetDistance / ANCHOR_SLOW_DOWN_DISTANCE);
                }
            }

            Advance(ref group, column, node, hasHeading, heading, targetSpeed);
        }

        /// <summary>
        /// 螺旋に並んでいたグループを、隊列に戻す。道筋を、向かう先 toTarget と逆向きにアンカーからまっすぐ伸ばして作り直し、メンバーが新しい隊列の位置へ向かえるようにする。
        /// 着いている間は道筋を記録していないので、そのままでは古い道筋に沿って並ぶ為。
        /// </summary>
        private void LeaveSpiral(int g, ref EnemyGroup group, float2 toTarget)
        {
            group.HasArrived = false;
            var yaw = math.lengthsq(toTarget) > 0f ? math.atan2(toTarget.x, toTarget.y) : group.AnchorYaw;
            var backward = -new float3(math.sin(yaw), 0f, math.cos(yaw));
            var count = math.min(EnemyGroups.PATH_CAPACITY,
                (int)math.ceil(GetFormationLength(group) / EnemyGroups.PATH_SPACING) + 1);
            var offset = g * EnemyGroups.PATH_CAPACITY;
            for (var i = 0; i < count; i++)
            {
                // 最も古い点が最も後ろになるよう、区画の先頭から後ろの点を並べる
                Paths[offset + i] = group.AnchorPosition + backward * ((count - 1 - i) * EnemyGroups.PATH_SPACING);
            }

            group.PathHead = count - 1;
            group.PathCount = count;
            group.AnchorYaw = yaw;
        }

        /// <summary>
        /// 帰還中のアンカーを、帰りの道筋の最も新しい点へ、点がなくなったら持ち場へ歩かせる。点に近づいたら、その点を捨てる。
        /// 持ち場に着いたか（HasReachedHome）と、進めないか（IsReturnBlocked）を書く。
        /// </summary>
        private void MoveHome(int g, ref EnemyGroup group)
        {
            group.HasReachedHome = false;
            group.IsReturnBlocked = false;
            if (!TryFitToFloor(ref group, out var column, out var node))
            {
                group.IsReturnBlocked = true;
                return;
            }

            var toTarget = (GetReturnTarget(g, group) - group.AnchorPosition).xz;
            var targetDistance = math.length(toTarget);
            while (group.ReturnCount > 0 && targetDistance <= RETURN_POINT_REACH_DISTANCE)
            {
                group.ReturnHead = (group.ReturnHead - 1 + EnemyGroups.RETURN_PATH_CAPACITY) % EnemyGroups.RETURN_PATH_CAPACITY;
                group.ReturnCount--;
                toTarget = (GetReturnTarget(g, group) - group.AnchorPosition).xz;
                targetDistance = math.length(toTarget);
            }

            if (group.ReturnCount == 0 && targetDistance <= ANCHOR_ARRIVE_DISTANCE)
            {
                group.HasReachedHome = true;
                group.AnchorSpeed = 0f;
                return;
            }

            var height = Grid.Heights[node];
            var hasHeading = TryGetHeadingToward(group.AnchorPosition, column, height, group.AnchorDistance, toTarget,
                out var heading);
            var isSlowingDown = group.ReturnCount == 0 && targetDistance < ANCHOR_SLOW_DOWN_DISTANCE;
            var targetSpeed = hasHeading
                ? MoveSpeed * Formation.AnchorSpeedRate * (isSlowingDown ? targetDistance / ANCHOR_SLOW_DOWN_DISTANCE : 1f)
                : 0f;
            Advance(ref group, column, node, hasHeading, heading, targetSpeed);

            group.IsReturnBlocked = !isSlowingDown && group.AnchorSpeed < STOPPED_SPEED;
        }

        /// <summary>
        /// 待機中のアンカーを、床の高さに合わせたまま止める。
        /// </summary>
        private void HoldAnchor(ref EnemyGroup group)
        {
            group.HasArrived = false;
            if (!TryFitToFloor(ref group, out _, out _)) return;

            group.AnchorSpeed = MoveTowards(group.AnchorSpeed, 0f, Formation.AnchorAcceleration * DeltaTime);
        }

        /// <summary>
        /// アンカーの速さを targetSpeed へ近づけ、向きを heading の方へ回し、向いている方へ進める。進んだ先の列に乗れなければ止める。
        /// </summary>
        private void Advance(ref EnemyGroup group, int column, int node, bool hasHeading, float2 heading, float targetSpeed)
        {
            var height = Grid.Heights[node];
            group.AnchorSpeed = MoveTowards(group.AnchorSpeed, targetSpeed, Formation.AnchorAcceleration * DeltaTime);

            if (hasHeading && math.lengthsq(heading) > 0f)
            {
                var desiredYaw = math.atan2(heading.x, heading.y);
                var maxTurn = math.radians(Formation.AnchorTurnSpeed) * DeltaTime;
                var yawDelta = math.atan2(math.sin(desiredYaw - group.AnchorYaw), math.cos(desiredYaw - group.AnchorYaw));
                group.AnchorYaw += math.clamp(yawDelta, -maxTurn, maxTurn);
            }

            if (group.AnchorSpeed <= 0f) return;

            var forward = new float3(math.sin(group.AnchorYaw), 0f, math.cos(group.AnchorYaw));
            var next = group.AnchorPosition + forward * (group.AnchorSpeed * DeltaTime);
            if (!Grid.TryGetColumn(next, out var movedColumn)) return;

            var landing = movedColumn == column ? node : Grid.GetLandingNode(movedColumn, height);
            if (landing < 0)
            {
                group.AnchorSpeed = 0f;
                return;
            }

            group.AnchorPosition = new float3(next.x, Grid.Heights[landing], next.z);
        }

        /// <summary>
        /// 帰還中のアンカーが向かう点。帰りの道筋の最も新しい点で、点がなければ持ち場。
        /// </summary>
        private float3 GetReturnTarget(int g, in EnemyGroup group)
        {
            return group.ReturnCount > 0
                ? ReturnPaths[g * EnemyGroups.RETURN_PATH_CAPACITY + group.ReturnHead]
                : group.HomePosition;
        }

        /// <summary>
        /// 追跡中のアンカーが帰りの道筋の最も新しい点から RETURN_PATH_SPACING 以上離れたら、点を足す。
        /// </summary>
        private void RecordReturnPath(int g, ref EnemyGroup group)
        {
            if (group.ReturnCount > 0
                && math.distancesq(GetReturnTarget(g, group), group.AnchorPosition)
                < EnemyGroups.RETURN_PATH_SPACING * EnemyGroups.RETURN_PATH_SPACING) return;

            PushReturnPoint(g, ref group, group.AnchorPosition);
        }

        /// <summary>
        /// 帰りの道筋に点を積む。あふれたら最も古い点を捨てる。
        /// </summary>
        private void PushReturnPoint(int g, ref EnemyGroup group, float3 point)
        {
            group.ReturnHead = (group.ReturnHead + 1) % EnemyGroups.RETURN_PATH_CAPACITY;
            ReturnPaths[g * EnemyGroups.RETURN_PATH_CAPACITY + group.ReturnHead] = point;
            group.ReturnCount = math.min(group.ReturnCount + 1, EnemyGroups.RETURN_PATH_CAPACITY);
        }

        /// <summary>
        /// 包囲の置き場の、プレイヤーからの螺旋の半径（m）。
        /// </summary>
        private float GetEncircleRadius(int slot)
        {
            return EnemyFormationSettings.GetSpiralRadius(slot, Formation.EncircleInnerRadius, Formation.EncircleLoopSpacing,
                Formation.EncircleSlotSpacing);
        }

        /// <summary>
        /// 置き場ごとに、プレイヤーを中心にした螺旋の上の位置を、プレイヤーへたどり着ける立てる層のある列へずらして SlotPoints に書き、
        /// 使える置き場のうち最も外の置き場の番号を LastUsableSlot に書く（なければ -1）。
        /// それより内側で、周り SLOT_SHIFT_RADIUS に立てる列がない置き場（建物の中や穴の上）は NaN にする。
        /// それより外の置き場は、置き場を持てないグループが待つ置き場にする。追跡範囲の外に出る位置は範囲の中へ寄せてから、立てる列へずらす
        /// （ずらせなければ寄せた位置のまま）。待つグループどうしも、置き場と同じ間隔をあける為。
        /// </summary>
        private void BuildSlotPoints()
        {
            var slotPoints = SlotPoints;
            var lastUsableSlot = -1;
            var searchRadius = (int)math.ceil(SLOT_SHIFT_RADIUS / Grid.CellSize);
            for (var slot = 0; slot < slotPoints.Length; slot++)
            {
                var point = GetSpiralPoint(slot);
                if (!TryShiftToReachableColumn(point, searchRadius, out var shifted))
                {
                    slotPoints[slot] = new float2(float.NaN);
                    continue;
                }

                slotPoints[slot] = shifted;
                lastUsableSlot = slot;
            }

            var areaMin = Grid.Origin.xz + ((float2)TrackingMin + 0.5f) * Grid.CellSize;
            var areaMax = Grid.Origin.xz + ((float2)TrackingMax + 0.5f) * Grid.CellSize;
            for (var slot = lastUsableSlot + 1; slot < slotPoints.Length; slot++)
            {
                var point = math.clamp(GetSpiralPoint(slot), areaMin, areaMax);
                slotPoints[slot] = TryShiftToReachableColumn(point, searchRadius, out var shifted) ? shifted : point;
            }

            LastUsableSlot[0] = lastUsableSlot;
        }

        /// <summary>
        /// プレイヤーを中心にした螺旋の上の、置き場の位置（水平）。
        /// </summary>
        private float2 GetSpiralPoint(int slot)
        {
            return PlayerPosition.xz + EnemyFormationSettings.GetSpiralOffset(slot, Formation.EncircleInnerRadius,
                Formation.EncircleLoopSpacing, Formation.EncircleSlotSpacing);
        }

        /// <summary>
        /// point の列から searchRadius 列までを近い順に調べ、プレイヤーへたどり着ける層のある最初の周の中で、point に最も近い列の中心を返す。
        /// point の列にあれば point をそのまま返す。
        /// </summary>
        private bool TryShiftToReachableColumn(float2 point, int searchRadius, out float2 shifted)
        {
            shifted = point;
            var origin = Grid.Origin.xz;
            var x = (int)math.floor((point.x - origin.x) / Grid.CellSize);
            var z = (int)math.floor((point.y - origin.y) / Grid.CellSize);

            for (var radius = 0; radius <= searchRadius; radius++)
            {
                var bestDistanceSq = float.MaxValue;
                for (var dz = -radius; dz <= radius; dz++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != radius) continue;

                        var nx = x + dx;
                        var nz = z + dz;
                        if (nx < TrackingMin.x || nx > TrackingMax.x || nz < TrackingMin.y || nz > TrackingMax.y) continue;

                        var column = nz * Grid.Width + nx;
                        if (!HasReachableNode(column)) continue;

                        var center = Grid.GetCellCenter(column, 0f).xz;
                        var distanceSq = math.distancesq(center, point);
                        if (distanceSq >= bestDistanceSq) continue;

                        bestDistanceSq = distanceSq;
                        shifted = radius == 0 ? point : center;
                    }
                }

                if (bestDistanceSq < float.MaxValue) return true;
            }

            return false;
        }

        /// <summary>
        /// 水平の位置 point の列の、プレイヤーまでの経路の長さ（m）。列に複数の層があれば最も短いもの。格子の外か、たどり着ける層がなければ、プレイヤーまでの直線の距離。
        /// </summary>
        private float GetPathDistance(float2 point)
        {
            var fallback = math.distance(point, PlayerPosition.xz);
            if (!Grid.TryGetColumn(new float3(point.x, 0f, point.y), out var column)) return fallback;

            var best = float.PositiveInfinity;
            for (var k = 0; k < Grid.LayerCounts[column]; k++)
            {
                best = math.min(best, Distances[column * EnemyNavigationGrid.MAX_LAYERS + k]);
            }

            return float.IsPositiveInfinity(best) ? fallback : best;
        }

        private bool HasReachableNode(int column)
        {
            for (var k = 0; k < Grid.LayerCounts[column]; k++)
            {
                if (!float.IsPositiveInfinity(Distances[column * EnemyNavigationGrid.MAX_LAYERS + k])) return true;
            }

            return false;
        }

        /// <summary>
        /// 置き場への向き toSlot へ、まっすぐ 1 マス進めればその向きを、進めなければ置き場に近づく隣の列への向きを、それもなければ距離マップの値が下がる隣の列への向きを返す。
        /// </summary>
        private bool TryGetHeadingToward(float3 position, int column, float height, float distance, float2 toSlot,
            out float2 heading)
        {
            heading = toSlot;
            var probe = position.xz + math.normalizesafe(toSlot) * Grid.CellSize;
            if (!Grid.IsBlocked(column, height, probe)) return true;

            if (Grid.TryGetColumnToward(column, height, position.xz + toSlot, out var towardColumn)
                || (!float.IsPositiveInfinity(distance)
                    && Grid.TryGetDownhillColumn(column, height, distance, Distances, out towardColumn)))
            {
                heading = (Grid.GetCellCenter(towardColumn, 0f) - position).xz;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 距離マップを下るアンカーの向き（水平）を返す。距離マップの値が下がる列を LOOKAHEAD_STEPS 列先までたどり、その列の中心へ向ける。
        /// 隣の列だけを見ると、向きが格子の 8 方向に限られ、隊列がまっすぐの線に並んで見える為。
        /// その向きへ 1 マス進んだ先に乗れなければ、隣の列の中心へ向ける（壁の角に引っかからない為）。
        /// </summary>
        private float2 GetDownhillHeading(float3 position, int column, float height, int nextColumn)
        {
            var toNext = (Grid.GetCellCenter(nextColumn, 0f) - position).xz;
            var farColumn = nextColumn;
            var farHeight = height;

            for (var step = 1; step < LOOKAHEAD_STEPS; step++)
            {
                var landing = Grid.GetLandingNode(farColumn, farHeight);
                if (landing < 0) break;

                farHeight = Grid.Heights[landing];
                if (!Grid.TryGetDownhillColumn(farColumn, farHeight, Distances[landing], Distances, out var further)) break;

                farColumn = further;
            }

            var toFar = (Grid.GetCellCenter(farColumn, 0f) - position).xz;
            var probe = position.xz + math.normalizesafe(toFar) * Grid.CellSize;
            return Grid.IsBlocked(column, height, probe) ? toNext : toFar;
        }

        /// <summary>
        /// グループの隊列の、アンカーから最後尾の列までの道筋に沿った長さ（m）。
        /// </summary>
        private float GetFormationLength(in EnemyGroup group)
        {
            var rows = (group.MemberCount + math.max(1, group.ColumnCount) - 1) / math.max(1, group.ColumnCount);
            return math.max(0, rows - 1) * Formation.RowSpacing;
        }

        /// <summary>
        /// アンカーが最も新しい点から PATH_SPACING 以上離れたら、道筋に点を足す。
        /// </summary>
        private void RecordPath(int g, ref EnemyGroup group)
        {
            var offset = g * EnemyGroups.PATH_CAPACITY;
            if (group.PathCount > 0
                && math.distancesq(Paths[offset + group.PathHead], group.AnchorPosition)
                < EnemyGroups.PATH_SPACING * EnemyGroups.PATH_SPACING) return;

            group.PathHead = (group.PathHead + 1) % EnemyGroups.PATH_CAPACITY;
            Paths[offset + group.PathHead] = group.AnchorPosition;
            group.PathCount = math.min(group.PathCount + 1, EnemyGroups.PATH_CAPACITY);
        }

        /// <summary>
        /// アンカーの位置と、進む先 COLUMN_LOOKAHEAD_DISTANCE までの COLUMN_LOOKAHEAD_INTERVAL おきの点で、左右へ同じ高さの床が続く幅を調べ、
        /// 最も狭い幅で 1 列に並べる数を決める。上限は MaxColumns。新しい数が ColumnChangeDelay の間続いたら変える。
        /// 進む先を見るのは、細い所に入ってから組み替えると、列の端のメンバーが壁に詰まる為。
        /// 進む先は、距離マップを下っている間は値が下がる列をたどり、それ以外（置き場へ向かう間、帰還中）はアンカーの向きの直線をたどる。
        /// </summary>
        private void UpdateColumnCount(ref EnemyGroup group)
        {
            var maxColumns = Formation.MaxColumns;
            var maxSide = (maxColumns - 1) * Formation.LateralSpacing * 0.5f;
            var forward = new float2(math.sin(group.AnchorYaw), math.cos(group.AnchorYaw));
            var width = MeasureFloorWidth(group.AnchorPosition, forward, maxSide);

            if (IsDescending(group) && Grid.TryGetColumn(group.AnchorPosition, out var column))
            {
                width = math.min(width, MeasureDownhillWidth(group.AnchorPosition, column, maxSide));
            }
            else
            {
                width = math.min(width, MeasureStraightWidth(group.AnchorPosition, forward, maxSide));
            }

            var columnCount = math.clamp(1 + (int)math.floor(width / Formation.LateralSpacing), 1, maxColumns);

            if (columnCount == group.ColumnCount)
            {
                group.PendingColumnCount = columnCount;
                group.PendingColumnTime = 0f;
                return;
            }

            if (columnCount != group.PendingColumnCount)
            {
                group.PendingColumnCount = columnCount;
                group.PendingColumnTime = 0f;
                return;
            }

            group.PendingColumnTime += DeltaTime;
            if (group.PendingColumnTime >= Formation.ColumnChangeDelay) group.ColumnCount = columnCount;
        }

        /// <summary>
        /// アンカーが距離マップを下っている（MoveAnchor と同じ条件で、置き場へまっすぐ向かう前の）間か。
        /// </summary>
        private bool IsDescending(in EnemyGroup group)
        {
            if (group.State != EnemyGroupState.Tracking || group.HasArrived || float.IsPositiveInfinity(group.AnchorDistance)) return false;
            if (group.EncircleSlot < 0) return true;

            var target = GetSpiralPoint(group.EncircleSlot);
            return group.AnchorDistance > GetPathDistance(target) + Formation.EncircleApproachMargin;
        }

        /// <summary>
        /// 点 point から、進む向き forward に直交する左右へ、point と同じ高さの床が続く幅（m）。片側は maxSide まで。
        /// </summary>
        private float MeasureFloorWidth(float3 point, float2 forward, float maxSide)
        {
            var right = new float2(forward.y, -forward.x);
            return Grid.GetFlatFloorLength(point, right, maxSide) + Grid.GetFlatFloorLength(point, -right, maxSide);
        }

        /// <summary>
        /// 距離マップの値が下がる列を COLUMN_LOOKAHEAD_DISTANCE 分たどり、COLUMN_LOOKAHEAD_INTERVAL おきに床の幅を調べて、最も狭い幅を返す。
        /// たどれなくなったら、そこまでの最も狭い幅を返す。
        /// </summary>
        private float MeasureDownhillWidth(float3 start, int column, float maxSide)
        {
            var minWidth = float.MaxValue;
            var height = start.y;
            var previous = start;
            var traveled = 0f;
            var nextSample = COLUMN_LOOKAHEAD_INTERVAL;

            while (traveled < COLUMN_LOOKAHEAD_DISTANCE)
            {
                var node = Grid.GetLandingNode(column, height);
                if (node < 0) break;

                height = Grid.Heights[node];
                if (!Grid.TryGetDownhillColumn(column, height, Distances[node], Distances, out var nextColumn)) break;

                var nextNode = Grid.GetLandingNode(nextColumn, height);
                if (nextNode < 0) break;

                var point = Grid.GetCellCenter(nextColumn, Grid.Heights[nextNode]);
                traveled += math.distance(previous.xz, point.xz);
                if (traveled >= nextSample)
                {
                    minWidth = math.min(minWidth, MeasureFloorWidth(point, math.normalizesafe((point - previous).xz), maxSide));
                    nextSample += COLUMN_LOOKAHEAD_INTERVAL;
                }

                previous = point;
                column = nextColumn;
            }

            return minWidth;
        }

        /// <summary>
        /// アンカーの向き forward の直線上を COLUMN_LOOKAHEAD_INTERVAL おきに COLUMN_LOOKAHEAD_DISTANCE まで進み、床の幅を調べて最も狭い幅を返す。
        /// 乗れない列に当たったら、そこまでの最も狭い幅を返す。
        /// </summary>
        private float MeasureStraightWidth(float3 start, float2 forward, float maxSide)
        {
            var minWidth = float.MaxValue;
            var point = start;
            for (var distance = COLUMN_LOOKAHEAD_INTERVAL; distance <= COLUMN_LOOKAHEAD_DISTANCE; distance += COLUMN_LOOKAHEAD_INTERVAL)
            {
                var next = start + new float3(forward.x, 0f, forward.y) * distance;
                if (!Grid.TryGetColumn(point, out var column) || !Grid.TryGetColumn(next, out var nextColumn)) break;

                var node = column == nextColumn ? -1 : Grid.GetLandingNode(nextColumn, point.y);
                if (column != nextColumn && node < 0) break;

                if (node >= 0) next.y = Grid.Heights[node];
                point = next;
                minWidth = math.min(minWidth, MeasureFloorWidth(point, forward, maxSide));
            }

            return minWidth;
        }

        public void Execute()
        {
            BuildSlotPoints();
            for (var g = 0; g < Groups.Length; g++)
            {
                var group = Groups[g];
                if (!group.IsActive) continue;

                if (!HasAliveMember(g, group))
                {
                    group.IsActive = false;
                    Groups[g] = group;
                    continue;
                }

                ApplyCommand(g, ref group);
                UpdateDormant(g, ref group);
                if (group.IsDormant)
                {
                    Groups[g] = group;
                    continue;
                }

                switch (group.State)
                {
                    case EnemyGroupState.Tracking:
                        MoveAnchor(g, ref group);
                        RecordReturnPath(g, ref group);
                        break;
                    case EnemyGroupState.Returning:
                        MoveHome(g, ref group);
                        break;
                    default:
                        HoldAnchor(ref group);
                        break;
                }

                RecordPath(g, ref group);
                UpdateColumnCount(ref group);
                Groups[g] = group;
            }
        }
    }
}
