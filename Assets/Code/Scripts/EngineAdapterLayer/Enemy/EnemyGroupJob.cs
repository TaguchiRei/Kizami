using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループを 1 つずつ更新する。進む・待つの切り替え、アンカーの移動と道筋の記録、隊列の 1 列の数、包囲の置き場の割り当てを行う。
    /// あわせて、交戦している敵へ、プレイヤーの周りの螺旋の上の置き場を割り当てる。
    /// 別のグループや別の敵の置き場を見るので、順番に依存しないよう、並列にせず 1 つの Job で回す。
    /// </summary>
    /// <remarks>
    /// アンカーは EnemyMoveJob の敵と同じ規則で距離マップを下るが、落ちずに床の高さへ合わせる。速さは敵より遅く、加速度の上限を持つ。
    /// プレイヤーまでの経路が包囲の距離以下になったアンカーは、プレイヤーを中心にした螺旋の上の置き場のうち、空いていて最も近いものを受け取り、そこへ向かう。
    /// 置き場はプレイヤーと一緒に動き、置き場が包囲を手放す距離より離れたら手放して、距離マップを下る状態に戻る。
    /// 先に近づいたグループが内側の置き場を取るので、グループは来た向きのまま、プレイヤーを内側から外側へ囲んでいく。
    /// 進む・待つの時間は、グループごとに固定の割合でずらし、グループどうしが同時に動き出さないようにする。
    /// 交戦する敵の置き場も螺旋の上に並べ（UnlimitedKnight の群衆の図）、空いていて敵に最も近いものを割り当てる。置き場は交戦に入る距離より内側だけを使う。
    /// </remarks>
    [BurstCompile]
    public struct EnemyGroupJob : IJob
    {
        /// <summary> 進む・待つの時間を、グループごとにずらす割合の幅（±） </summary>
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

        public NativeArray<EnemyGroup> Groups;

        /// <summary> グループごとに EnemyGroups.PATH_CAPACITY 個の区画を持つ道筋の点 </summary>
        public NativeArray<float3> Paths;

        /// <summary> グループごとに EnemyFormationSettings.MAX_GROUP_SIZE 個の区画を持つ、メンバーの敵の番号 </summary>
        [ReadOnly] public NativeArray<int> Members;

        /// <summary> 敵ごとの、交戦する敵の置き場の番号。持たなければ -1 </summary>
        public NativeArray<int> EngageSlots;

        [ReadOnly] public NativeArray<EnemyAgent> Agents;
        public EnemyNavigationGrid Grid;
        [ReadOnly] public NativeArray<float> Distances;
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

                UpdatePhase(g, ref group);
                MoveAnchor(g, ref group, usedEncircleSlots);
                RecordPath(g, ref group);
                UpdateColumnCount(ref group);
                Groups[g] = group;
            }

            usedEncircleSlots.Dispose();
            UpdateEngageSlots();
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
        /// アンカーを床の高さへ合わせ、進んでいる間は、包囲の置き場があればそこへ、なければ距離マップの値が下がる方へ歩かせる。
        /// 置き場に着いたら止まってプレイヤーを向く。
        /// 距離マップを下る間は、止まる距離に着いたとき、たどり着けないとき、同じレーンの前で別のグループが待つ番で止まっているときに、止まるまで減速する。
        /// </summary>
        private void MoveAnchor(int g, ref EnemyGroup group, NativeArray<bool> usedEncircleSlots)
        {
            if (!Grid.TryGetColumn(group.AnchorPosition, out var column))
            {
                group.AnchorSpeed = 0f;
                return;
            }

            var node = Grid.GetHighestNodeBelow(column, group.AnchorPosition.y + Grid.ClimbHeight);
            if (node < 0)
            {
                group.AnchorSpeed = 0f;
                return;
            }

            var height = Grid.Heights[node];
            var distance = Distances[node];
            group.AnchorPosition.y = height;
            group.AnchorDistance = distance;
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
        /// 置き場までの直線の距離が包囲を手放す距離を超えたら手放し、置き場がなくプレイヤーまでの経路が包囲の距離以下なら、空いていて最も近い置き場を受け取る。
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
        /// 包囲の置き場の位置（水平）。
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
        /// 前を行く相手が待つ番で止まったら、その後ろで止まる。着いて止まったグループ、詰まって止まったグループ、包囲の置き場を持つグループの後ろでは待たない（待ちが後ろへ連鎖して、全体が止まらないようにする為）。
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
                if (!other.IsActive || other.EncircleSlot >= 0 || other.IsAdvancing || other.AnchorSpeed > STOPPED_SPEED) continue;
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
        /// 使う置き場は、プレイヤーからの距離が交戦に入る距離以下のものだけ。空きがなければ、置き場を持たないまま待つ。
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
    }
}
