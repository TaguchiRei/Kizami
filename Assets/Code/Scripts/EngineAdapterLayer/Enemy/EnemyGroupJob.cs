using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループを 1 つずつ更新する。進む・待つの切り替え、アンカーの移動と道筋の記録、隊列の 1 列の数を決める。
    /// 前を行く別のグループのアンカーを見るので、グループどうしの順番に依存しないよう、並列にせず 1 つの Job で回す。
    /// </summary>
    /// <remarks>
    /// アンカーは EnemyMoveJob の敵と同じ規則で距離マップを下るが、落ちずに床の高さへ合わせる。速さは敵より遅く、加速度の上限を持つ。
    /// 進む・待つの時間は、グループごとに固定の割合でずらし、グループどうしが同時に動き出さないようにする。
    /// 1 列の数は、アンカーの位置で道筋の左右に立てるマスが続く幅から決める。
    /// </remarks>
    [BurstCompile]
    public struct EnemyGroupJob : IJob
    {
        /// <summary> アンカーが立っている層とみなす、位置より上の高さ（m） </summary>
        private const float GROUND_TOLERANCE = 0.05f;

        /// <summary> 進む・待つの時間を、グループごとにずらす割合の幅（±） </summary>
        private const float PHASE_JITTER = 0.3f;

        /// <summary> アンカーが止まっているとみなす速さ（m/s）。待つ番のグループがこれより遅くなると、同じレーンの後ろのグループは待つ </summary>
        private const float STOPPED_SPEED = 0.5f;

        /// <summary> アンカーが向かう先として、距離マップの値が下がる列をたどる数 </summary>
        private const int LOOKAHEAD_STEPS = 6;

        public NativeArray<EnemyGroup> Groups;

        /// <summary> グループごとに EnemyGroups.PATH_CAPACITY 個の区画を持つ道筋の点 </summary>
        public NativeArray<float3> Paths;

        /// <summary> グループごとに EnemyFormationSettings.MAX_GROUP_SIZE 個の区画を持つ、メンバーの敵の番号 </summary>
        [ReadOnly] public NativeArray<int> Members;

        [ReadOnly] public NativeArray<EnemyAgent> Agents;
        public EnemyNavigationGrid Grid;
        [ReadOnly] public NativeArray<float> Distances;
        public EnemyFormationSettings Formation;

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
                MoveAnchor(g, ref group);
                RecordPath(g, ref group);
                UpdateColumnCount(ref group);
                Groups[g] = group;
            }
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
        /// アンカーを床の高さへ合わせ、進んでいる間は距離マップの値が下がる隣の列へ向かって歩かせる。
        /// 止まる距離に着いたとき、たどり着けないとき、同じレーンの前で別のグループが待つ番で止まっているときは、止まるまで減速する。
        /// </summary>
        private void MoveAnchor(int g, ref EnemyGroup group)
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

            var nextColumn = -1;
            var hasNext = distance > Formation.AnchorStopDistance && !float.IsPositiveInfinity(distance)
                          && Grid.TryGetDownhillColumn(column, height, distance, Distances, out nextColumn);
            var targetSpeed = hasNext && group.IsAdvancing && !IsBlockedByGroupAhead(g, group)
                ? MoveSpeed * Formation.AnchorSpeedRate
                : 0f;
            group.AnchorSpeed = MoveTowards(group.AnchorSpeed, targetSpeed, Formation.AnchorAcceleration * DeltaTime);

            if (hasNext)
            {
                var toNext = GetHeading(group.AnchorPosition, column, height, nextColumn);
                var desiredYaw = math.atan2(toNext.x, toNext.y);
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
        /// アンカーが向かう向き（水平）を返す。距離マップの値が下がる列を LOOKAHEAD_STEPS 列先までたどり、その列の中心へ向ける。
        /// 隣の列だけを見ると、向きが格子の 8 方向に限られ、隊列がまっすぐの線に並んで見える為。
        /// その向きへ 1 マス進んだ先に乗れなければ、隣の列の中心へ向ける（壁の角に引っかからない為）。
        /// </summary>
        private float2 GetHeading(float3 position, int column, float height, int nextColumn)
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
            if (!Grid.TryGetColumn(new float3(probe.x, height, probe.y), out var probeColumn)) return toNext;
            if (probeColumn != column && Grid.GetLandingNode(probeColumn, height) < 0) return toNext;

            return toFar;
        }

        /// <summary>
        /// 同じレーンの前で、待つ番で止まっている別のグループがあるか。
        /// 同じレーンとは、このアンカーの前方で、相手の最後尾までがグループの間隔より近く、横のずれが 2 つの隊列の幅の半分の和より小さいこと。
        /// 前を行く相手が待つ番で止まったら、その後ろで止まる。着いて止まったグループや、詰まって止まったグループの後ろでは待たない（待ちが後ろへ連鎖して、全体が止まらないようにする為）。
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
                if (!other.IsActive || other.IsAdvancing || other.AnchorSpeed > STOPPED_SPEED) continue;
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
        /// アンカーの位置から左右へ、アンカーと同じ高さの床が続く幅を調べ、1 列に並べる数を決める。
        /// 新しい数が ColumnChangeDelay の間続いたら変える。
        /// </summary>
        private void UpdateColumnCount(ref EnemyGroup group)
        {
            var right = new float2(math.cos(group.AnchorYaw), -math.sin(group.AnchorYaw));
            var maxSide = (Formation.MaxColumns - 1) * Formation.LateralSpacing * 0.5f;
            var width = GetFreeLength(group.AnchorPosition, right, maxSide)
                        + GetFreeLength(group.AnchorPosition, -right, maxSide);
            var columnCount = math.clamp(1 + (int)math.floor(width / Formation.LateralSpacing), 1, Formation.MaxColumns);

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
        /// origin から direction へマスの一辺ずつ進み、origin と同じ高さの床が続く長さ（m）を maxLength まで返す。
        /// </summary>
        private float GetFreeLength(float3 origin, float2 direction, float maxLength)
        {
            var steps = (int)math.ceil(maxLength / Grid.CellSize);
            for (var s = 1; s <= steps; s++)
            {
                var length = math.min(s * Grid.CellSize, maxLength);
                var point = origin + new float3(direction.x, 0f, direction.y) * length;
                if (!Grid.TryGetColumn(point, out var column) || Grid.GetNodeNear(column, origin.y) < 0)
                {
                    return (s - 1) * Grid.CellSize;
                }
            }

            return maxLength;
        }
    }
}
