using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループを 1 つずつ更新する。待機・追跡・帰還の切り替え、進む・待つの切り替え、アンカーの移動と道筋・帰りの道筋の記録、隊列の 1 列の数、包囲の置き場の割り当てを行う。
    /// 持ち場が追跡範囲（距離マップを計算した範囲）に入れば追跡に、外れれば帰還にする。範囲が変わるのは区画の切り替えのときだけなので、毎フレーム調べても切り替えのときだけ変わる。
    /// あわせて、交戦している敵へ、プレイヤーの周りの螺旋の上の置き場を割り当てる。
    /// 別のグループや別の敵の置き場を見るので、順番に依存しないよう、並列にせず 1 つの Job で回す。
    /// </summary>
    [BurstCompile]
    public struct EnemyGroupJob : IJob
    {
        /// <summary> 進む・待つの時間を、グループごとにずらす割合の幅（±）。グループどうしが同時に動き出さないようにする </summary>
        private const float PHASE_JITTER = 0.3f;

        /// <summary> アンカーが止まっているとみなす速さ（m/s）。待つ番のグループがこれより遅くなると、同じレーンの後ろのグループは待つ </summary>
        private const float STOPPED_SPEED = 0.5f;

        /// <summary> アンカーが向かう先として、距離マップの値が下がる列をたどる数 </summary>
        private const int LOOKAHEAD_STEPS = 6;

        /// <summary> 包囲の置き場の数 </summary>
        private const int MAX_ENCIRCLE_SLOTS = 128;

        /// <summary> 交戦する敵の置き場の数の上限。実際に使うのは、交戦に入る距離より内側の置き場だけ </summary>
        private const int MAX_ENGAGE_SLOTS = 64;

        /// <summary> アンカーが包囲の置き場にこの距離（m）まで近づいたら、着いたとみなす </summary>
        private const float ANCHOR_ARRIVE_DISTANCE = 1f;

        /// <summary> アンカーが包囲の置き場までの距離がこの値（m）より近いと、近さに合わせて遅くなる </summary>
        private const float ANCHOR_SLOW_DOWN_DISTANCE = 4f;

        /// <summary> 帰還中のアンカーが帰りの道筋の点にこの距離（m）まで近づいたら、その点を捨てて次の点へ向かう </summary>
        private const float RETURN_POINT_REACH_DISTANCE = 2f;

        /// <summary> 帰還中のアンカーが進めない状態がこの時間（秒）続いたら、その位置を新しい持ち場にする </summary>
        private const float RETURN_BLOCKED_DURATION = 5f;

        public NativeArray<EnemyGroup> Groups;

        /// <summary> グループごとに EnemyGroups.PATH_CAPACITY 個の区画を持つ道筋の点 </summary>
        public NativeArray<float3> Paths;

        /// <summary> グループごとに EnemyGroups.RETURN_PATH_CAPACITY 個の区画を持つ帰りの道筋の点 </summary>
        public NativeArray<float3> ReturnPaths;

        /// <summary> グループごとに EnemyFormationSettings.MAX_GROUP_SIZE 個の区画を持つ、メンバーの敵の番号 </summary>
        [ReadOnly] public NativeArray<int> Members;

        /// <summary> 敵ごとの、交戦する敵の置き場の番号。持たなければ -1 </summary>
        public NativeArray<int> EngageSlots;

        [ReadOnly] public NativeArray<EnemyAgent> Agents;
        public EnemyNavigationGrid Grid;
        [ReadOnly] public NativeArray<float> Distances;

        /// <summary> Distances を計算した追跡範囲の、最小の列 (x, z) </summary>
        public int2 TrackingMin;

        /// <summary> Distances を計算した追跡範囲の、最大の列 (x, z)。この列も含む </summary>
        public int2 TrackingMax;

        public EnemyFormationSettings Formation;

        /// <summary> プレイヤーの位置。包囲と交戦の螺旋の中心 </summary>
        public float3 PlayerPosition;

        /// <summary> 敵が歩く速さ（m/s） </summary>
        public float MoveSpeed;

        public float DeltaTime;

        /// <summary>
        /// グループの番号から決まる 0〜1 の値。
        /// </summary>
        private static float GetGroupRandom(int g)
        {
            return (math.hash(new uint2((uint)g, 0x9E3779B9u)) & 0xFFFFu) / 65535f;
        }

        private static float MoveTowards(float current, float target, float maxDelta)
        {
            return current < target ? math.min(current + maxDelta, target) : math.max(current - maxDelta, target);
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

        private void UpdatePhase(int g, ref EnemyGroup group)
        {
            group.PhaseTimer -= DeltaTime;
            if (group.PhaseTimer > 0f) return;

            group.IsAdvancing = !group.IsAdvancing;
            var jitter = 1f + PHASE_JITTER * (2f * GetGroupRandom(g) - 1f);
            group.PhaseTimer += (group.IsAdvancing ? Formation.AdvanceDuration : Formation.HoldDuration) * jitter;
        }

        /// <summary>
        /// 持ち場が追跡範囲に入ったら追跡に、外れたら帰還にする。
        /// 追跡を始めるときは、帰りの道筋が空なら持ち場を最初の点にする。帰還を始めるときは包囲の置き場を手放す。
        /// どちらも、アンカーが向かう先と逆を向いていれば、隊列を前後に入れ替える。
        /// </summary>
        private void UpdateState(int g, ref EnemyGroup group, NativeArray<bool> usedEncircleSlots)
        {
            var isHomeTracked = IsTracked(group.HomePosition);

            if (group.State != EnemyGroupState.Tracking && isHomeTracked)
            {
                group.State = EnemyGroupState.Tracking;
                group.BlockedTime = 0f;
                if (group.ReturnCount == 0) PushReturnPoint(g, ref group, group.HomePosition);

                FaceFormation(g, ref group, (PlayerPosition - group.AnchorPosition).xz);
                return;
            }

            if (group.State != EnemyGroupState.Tracking || isHomeTracked) return;

            group.State = EnemyGroupState.Returning;
            group.BlockedTime = 0f;
            group.HasArrived = false;
            if (group.EncircleSlot >= 0)
            {
                usedEncircleSlots[group.EncircleSlot] = false;
                group.EncircleSlot = -1;
            }

            FaceFormation(g, ref group, (GetReturnTarget(g, group) - group.AnchorPosition).xz);
        }

        /// <summary>
        /// 位置の真下の列が、距離マップを計算した追跡範囲の中にあるか。
        /// </summary>
        private bool IsTracked(float3 position)
        {
            if (!Grid.TryGetColumn(position, out var column)) return false;

            var x = column % Grid.Width;
            var z = column / Grid.Width;
            return x >= TrackingMin.x && x <= TrackingMax.x && z >= TrackingMin.y && z <= TrackingMax.y;
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
        /// 追跡中のアンカーを、進んでいる間は、包囲の置き場があればそこへ、なければ距離マップの値が下がる方へ歩かせる。
        /// 置き場に着いたら止まってプレイヤーを向く。
        /// 距離マップを下る間は、止まる距離に着いたとき、たどり着けないとき、同じレーンの前で別のグループが待つ番で止まっているときに、止まるまで減速する。
        /// </summary>
        private void MoveAnchor(int g, ref EnemyGroup group, NativeArray<bool> usedEncircleSlots)
        {
            if (!TryFitToFloor(ref group, out var column, out var node)) return;

            var height = Grid.Heights[node];
            var distance = group.AnchorDistance;
            UpdateEncircleSlot(ref group, distance, usedEncircleSlots);

            var targetSpeed = 0f;
            var hasHeading = false;
            var heading = float2.zero;

            if (group.EncircleSlot >= 0)
            {
                var toSlot = GetEncirclePoint(group.EncircleSlot) - group.AnchorPosition.xz;
                var slotDistance = math.length(toSlot);
                group.HasArrived = slotDistance <= ANCHOR_ARRIVE_DISTANCE;

                if (group.HasArrived)
                {
                    heading = (PlayerPosition - group.AnchorPosition).xz;
                    hasHeading = true;
                }
                else if (TryGetHeadingToward(group.AnchorPosition, column, height, distance, toSlot, out heading))
                {
                    hasHeading = true;
                    if (group.IsAdvancing)
                    {
                        targetSpeed = MoveSpeed * Formation.AnchorSpeedRate
                                      * math.saturate(slotDistance / ANCHOR_SLOW_DOWN_DISTANCE);
                    }
                }
            }
            else
            {
                group.HasArrived = false;
                var nextColumn = -1;
                hasHeading = distance > Formation.AnchorStopDistance && !float.IsPositiveInfinity(distance)
                             && Grid.TryGetDownhillColumn(column, height, distance, Distances, out nextColumn);
                if (hasHeading)
                {
                    heading = GetDownhillHeading(group.AnchorPosition, column, height, nextColumn);
                    if (group.IsAdvancing && !IsBlockedByGroupAhead(g, group)) targetSpeed = MoveSpeed * Formation.AnchorSpeedRate;
                }
            }

            Advance(ref group, column, node, hasHeading, heading, targetSpeed);
        }

        /// <summary>
        /// 帰還中のアンカーを、帰りの道筋の最も新しい点へ、点がなくなったら持ち場へ歩かせる。点に近づいたら、その点を捨てる。
        /// 持ち場に着いたら待機にする。
        /// </summary>
        private void MoveHome(int g, ref EnemyGroup group)
        {
            if (!TryFitToFloor(ref group, out var column, out var node))
            {
                UpdateBlockedTime(ref group, true);
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
                group.State = EnemyGroupState.Waiting;
                group.AnchorSpeed = 0f;
                group.BlockedTime = 0f;
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

            UpdateBlockedTime(ref group, !isSlowingDown && group.AnchorSpeed < STOPPED_SPEED);
        }

        /// <summary>
        /// 帰還中に進めない時間を数え、RETURN_BLOCKED_DURATION を超えたら、アンカーの位置を新しい持ち場にして待機にする。持ち場が追跡範囲の中なら、次のフレームで追跡に戻る。
        /// 元の持ち場へ移さないのは、持ち場が埋まっていることがあり、プレイヤーが敵を分断する遊び（橋を切るなど）を残す為。
        /// </summary>
        private void UpdateBlockedTime(ref EnemyGroup group, bool isBlocked)
        {
            group.BlockedTime = isBlocked ? group.BlockedTime + DeltaTime : 0f;
            if (group.BlockedTime < RETURN_BLOCKED_DURATION) return;

            group.HomePosition = group.AnchorPosition;
            group.ReturnCount = 0;
            group.BlockedTime = 0f;
            group.State = EnemyGroupState.Waiting;
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
        /// 置き場までの直線の距離が包囲を手放す距離を超えたら手放し、置き場がなくプレイヤーまでの経路が包囲の距離以下なら、空いていて最も近い置き場を受け取る。
        /// 先に近づいたグループが内側の置き場を取るので、グループは来た向きのまま、プレイヤーを内側から外側へ囲んでいく。
        /// </summary>
        private void UpdateEncircleSlot(ref EnemyGroup group, float distance, NativeArray<bool> usedEncircleSlots)
        {
            if (group.EncircleSlot >= 0)
            {
                var leaveSq = Formation.EncircleLeaveDistance * Formation.EncircleLeaveDistance;
                if (math.distancesq(GetEncirclePoint(group.EncircleSlot), group.AnchorPosition.xz) <= leaveSq) return;

                usedEncircleSlots[group.EncircleSlot] = false;
                group.EncircleSlot = -1;
                group.HasArrived = false;
            }

            if (distance > Formation.EncircleDistance) return;

            var bestDistanceSq = float.MaxValue;
            for (var slot = 0; slot < MAX_ENCIRCLE_SLOTS; slot++)
            {
                if (usedEncircleSlots[slot]) continue;

                var distanceSq = math.distancesq(GetEncirclePoint(slot), group.AnchorPosition.xz);
                if (distanceSq >= bestDistanceSq) continue;

                bestDistanceSq = distanceSq;
                group.EncircleSlot = slot;
            }

            if (group.EncircleSlot >= 0) usedEncircleSlots[group.EncircleSlot] = true;
        }

        /// <summary>
        /// 包囲の置き場の位置（水平）。プレイヤーを中心にした螺旋の上にあり、プレイヤーと一緒に動く。
        /// </summary>
        private float2 GetEncirclePoint(int slot)
        {
            return PlayerPosition.xz + EnemyFormationSettings.GetSpiralOffset(slot, Formation.EncircleInnerRadius,
                Formation.EncircleLoopSpacing, Formation.EncircleSlotSpacing);
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
        /// 同じレーンの前で、待つ番で止まっている別のグループがあるか。
        /// 同じレーンとは、このアンカーの前方で、相手の最後尾までがグループの間隔より近く、横のずれが 2 つの隊列の幅の半分の和より小さいこと。
        /// 前を行く相手が待つ番で止まったら、その後ろで止まる。着いて止まったグループ、詰まって止まったグループ、包囲の置き場を持つグループ、追跡していないグループの後ろでは待たない（待ちが後ろへ連鎖して、全体が止まらないようにする為）。
        /// 前後は、プレイヤーまでの経路が短い方を前とし、同じなら番号の小さい方を前とする。
        /// </summary>
        private bool IsBlockedByGroupAhead(int g, in EnemyGroup group)
        {
            var forward = new float2(math.sin(group.AnchorYaw), math.cos(group.AnchorYaw));
            var halfWidth = GetFormationHalfWidth(group);

            for (var h = 0; h < Groups.Length; h++)
            {
                if (h == g) continue;

                var other = Groups[h];
                if (!other.IsActive || other.State != EnemyGroupState.Tracking || other.EncircleSlot >= 0 || other.IsAdvancing || other.AnchorSpeed > STOPPED_SPEED) continue;
                if (other.AnchorDistance > group.AnchorDistance
                    || (other.AnchorDistance == group.AnchorDistance && h > g)) continue;

                var toOther = other.AnchorPosition.xz - group.AnchorPosition.xz;
                var along = math.dot(forward, toOther);
                if (along <= 0f || along > Formation.GroupSpacing + GetFormationLength(other)) continue;

                var across = math.abs(forward.x * toOther.y - forward.y * toOther.x);
                if (across < halfWidth + GetFormationHalfWidth(other)) return true;
            }

            return false;
        }

        /// <summary>
        /// グループの隊列の幅の半分（m）。隣の隊列との間に、横の間隔の半分をあける。
        /// </summary>
        private float GetFormationHalfWidth(in EnemyGroup group)
        {
            return math.max(1, group.ColumnCount) * Formation.LateralSpacing * 0.5f;
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
        /// アンカーの位置から左右へ、アンカーと同じ高さの床が続く幅を調べ、1 列に並べる数を決める。上限は、包囲の間は EncircleColumns、それ以外は MaxColumns。
        /// 新しい数が ColumnChangeDelay の間続いたら変える。
        /// </summary>
        private void UpdateColumnCount(ref EnemyGroup group)
        {
            var maxColumns = group.EncircleSlot >= 0 ? Formation.EncircleColumns : Formation.MaxColumns;
            var right = new float2(math.cos(group.AnchorYaw), -math.sin(group.AnchorYaw));
            var maxSide = (maxColumns - 1) * Formation.LateralSpacing * 0.5f;
            var width = Grid.GetFlatFloorLength(group.AnchorPosition, right, maxSide)
                        + Grid.GetFlatFloorLength(group.AnchorPosition, -right, maxSide);
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
        /// 交戦をやめた敵と消えた敵の置き場を空け、置き場を持たない交戦中の敵へ、空いていて最も近い置き場を割り当てる。
        /// 置き場はプレイヤーを中心にした螺旋の上に並べ（UnlimitedKnight の群衆の図）、使うのは、プレイヤーからの距離が交戦に入る距離以下のものだけ。
        /// 空きがなければ、置き場を持たないまま待つ。
        /// </summary>
        private void UpdateEngageSlots()
        {
            var slotCount = 0;
            while (slotCount < MAX_ENGAGE_SLOTS
                   && EnemyFormationSettings.GetSpiralRadius(slotCount, Formation.SpiralInnerRadius,
                       Formation.SpiralLoopSpacing, Formation.SpiralSlotSpacing) <= Formation.EngageEnterDistance)
            {
                slotCount++;
            }

            var usedSlots = new NativeArray<bool>(MAX_ENGAGE_SLOTS, Allocator.Temp);
            for (var i = 0; i < Agents.Length; i++)
            {
                var slot = EngageSlots[i];
                if (slot < 0) continue;

                var agent = Agents[i];
                if (!agent.IsAlive || !agent.IsEngaged || slot >= slotCount || usedSlots[slot])
                {
                    EngageSlots[i] = -1;
                    continue;
                }

                usedSlots[slot] = true;
            }

            for (var i = 0; i < Agents.Length; i++)
            {
                var agent = Agents[i];
                if (!agent.IsAlive || !agent.IsEngaged || EngageSlots[i] >= 0) continue;

                var best = -1;
                var bestDistanceSq = float.MaxValue;
                for (var slot = 0; slot < slotCount; slot++)
                {
                    if (usedSlots[slot]) continue;

                    var point = PlayerPosition.xz + EnemyFormationSettings.GetSpiralOffset(slot, Formation.SpiralInnerRadius,
                        Formation.SpiralLoopSpacing, Formation.SpiralSlotSpacing);
                    var distanceSq = math.distancesq(point, agent.Position.xz);
                    if (distanceSq >= bestDistanceSq) continue;

                    bestDistanceSq = distanceSq;
                    best = slot;
                }

                if (best < 0) break;

                usedSlots[best] = true;
                EngageSlots[i] = best;
            }

            usedSlots.Dispose();
        }

        public void Execute()
        {
            var usedEncircleSlots = new NativeArray<bool>(MAX_ENCIRCLE_SLOTS, Allocator.Temp);
            for (var g = 0; g < Groups.Length; g++)
            {
                var group = Groups[g];
                if (group.IsActive && group.EncircleSlot >= 0) usedEncircleSlots[group.EncircleSlot] = true;
            }

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

                UpdateState(g, ref group, usedEncircleSlots);
                switch (group.State)
                {
                    case EnemyGroupState.Tracking:
                        UpdatePhase(g, ref group);
                        MoveAnchor(g, ref group, usedEncircleSlots);
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

            usedEncircleSlots.Dispose();
            UpdateEngageSlots();
        }
    }
}
