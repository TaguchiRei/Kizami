using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 出ている敵を 1 体ずつ動かす。グループに入っている敵は隊列の位置（置き場に着いたグループでは、グループの中心の周りの螺旋の上の位置）へ、
    /// グループを持たない敵は距離マップの値が下がる隣の列へ向かって歩き、立てる層がなくなると落ちる。
    /// 止められた敵（EnemyMoveMode.Held）は歩かず、飛んでいる敵（Flying）は地面と重力によらず FlyTarget へ飛ぶ。
    /// 自分の番号の敵だけを書き換え、ほかの敵は見ない。
    /// </summary>
    [BurstCompile]
    public struct EnemyMoveJob : IJobParallelFor
    {
        /// <summary> 立ったまま床に合わせて下りる高さ（m）。これより低い床へは落ちる </summary>
        private const float STEP_DOWN_HEIGHT = 0.5f;

        /// <summary> 立っている層とみなす、位置より上の高さ（m） </summary>
        private const float GROUND_TOLERANCE = 0.05f;

        /// <summary> 真下の列に着地できる層がないときに、着地先を探す周りの列の数。幅 6m ほどの橋の下からでも、外の床に届くようにする </summary>
        private const int LANDING_SEARCH_RADIUS = 4;

        /// <summary> 周りの列に着地したときに、位置をマスの縁から離す距離（m） </summary>
        private const float COLUMN_EDGE_MARGIN = 0.05f;

        /// <summary> 目指す位置にこの距離（m）まで近づいたら、着いたとみなして止まる </summary>
        private const float ARRIVE_DISTANCE = 0.3f;

        /// <summary> 目指す位置までの距離がこの値（m）より近いと、近さに合わせて遅くなる </summary>
        private const float SLOW_DOWN_DISTANCE = 2f;

        /// <summary> 歩く速さを敵ごとにずらす割合の幅（±）。全員が同じ速さで動いて見えないようにする </summary>
        private const float SPEED_JITTER = 0.1f;

        /// <summary> 飛ぶ先までの距離がこの値（m）より近いと、近さに合わせて遅くなる </summary>
        private const float FLY_SLOW_DOWN_DISTANCE = 2f;

        /// <summary> 飛ぶ先の近くで遅くなるときの、速さの下限（m/s）。飛ぶ先に届かなくならないようにする </summary>
        private const float MIN_FLY_SPEED = 1f;

        public NativeArray<EnemyAgent> Agents;
        public EnemyNavigationGrid Grid;
        [ReadOnly] public NativeArray<float> Distances;

        /// <summary> Distances を計算した追跡範囲の、最小の列 (x, z) </summary>
        public int2 TrackingMin;

        /// <summary> Distances を計算した追跡範囲の、最大の列 (x, z)。この列も含む </summary>
        public int2 TrackingMax;

        /// <summary> グループの状態 </summary>
        [ReadOnly] public NativeArray<EnemyGroup> Groups;

        /// <summary> グループごとに EnemyGroups.PATH_CAPACITY 個の区画を持つ道筋の点 </summary>
        [ReadOnly] public NativeArray<float3> Paths;

        public EnemyFormationSettings Formation;

        /// <summary> プレイヤーの位置。着いたグループのメンバーと、近づきすぎて止まった敵が向く </summary>
        public float3 PlayerPosition;

        public float DeltaTime;

        /// <summary> 歩く速さ（m/s） </summary>
        public float MoveSpeed;

        /// <summary> 飛ぶ速さ（m/s） </summary>
        public float FlySpeed;

        /// <summary> 向きを変える速さ（ラジアン/秒） </summary>
        public float TurnSpeed;

        /// <summary> 距離マップの値がこれ以下（m）なら止まる </summary>
        public float StopDistance;

        /// <summary> 重力の加速度の大きさ（m/s²） </summary>
        public float Gravity;

        /// <summary> この高さ（m）以上落ちて着地した敵は、崩落で倒されたとする </summary>
        public float FallDefeatHeight;

        /// <summary>
        /// 敵の番号から決まる 0〜1 の値。
        /// </summary>
        private static float GetAgentRandom(int index)
        {
            return (math.hash(new uint2((uint)index, 0x85EBCA6Bu)) & 0xFFFFu) / 65535f;
        }

        private static void DefeatByCollapse(ref EnemyAgent agent)
        {
            agent.IsAlive = false;
            agent.IsDefeatedByCollapse = true;
        }

        /// <summary>
        /// 追跡範囲の中で、立っている層からプレイヤーへたどり着けない（距離マップの値がない）間、その時間を数える。
        /// たどり着けるか、追跡範囲の外にいれば 0 に戻す。範囲の外の層は距離を持たない為。格子の範囲の外にいる間は数え、落ちている間は数えた時間をそのまま持つ。
        /// </summary>
        private void UpdateStrandedTime(ref EnemyAgent agent)
        {
            if (!agent.IsAlive || !agent.IsGrounded) return;

            var isStranded = !Grid.TryGetColumn(agent.Position, out var column);
            if (!isStranded && IsInTrackingRange(column))
            {
                var node = Grid.GetHighestNodeBelow(column, agent.Position.y + GROUND_TOLERANCE);
                isStranded = node < 0 || float.IsPositiveInfinity(Distances[node]);
            }

            agent.StrandedTime = isStranded ? agent.StrandedTime + DeltaTime : 0f;
        }

        private bool IsInTrackingRange(int column)
        {
            var x = column % Grid.Width;
            var z = column / Grid.Width;
            return x >= TrackingMin.x && x <= TrackingMax.x && z >= TrackingMin.y && z <= TrackingMax.y;
        }

        private void Walk(ref EnemyAgent agent, int index)
        {
            if (!Grid.TryGetColumn(agent.Position, out var column)) return;

            var node = Grid.GetHighestNodeBelow(column, agent.Position.y + GROUND_TOLERANCE);
            if (node < 0) return;

            var height = Grid.Heights[node];
            var distance = Distances[node];

            var toPlayer = (PlayerPosition - agent.Position).xz;
            var yawToPlayer = math.atan2(toPlayer.x, toPlayer.y);
            if (distance <= StopDistance)
            {
                Turn(ref agent, yawToPlayer);
                return;
            }

            var speed = MoveSpeed * (1f + SPEED_JITTER * (2f * GetAgentRandom(index) - 1f));

            if (TryGetSlotTarget(agent, out var target, out var slotYaw))
            {
                MoveToward(ref agent, column, height, distance, target, slotYaw, speed);
                return;
            }

            if (float.IsPositiveInfinity(distance)) return;
            if (!Grid.TryGetDownhillColumn(column, height, distance, Distances, out var nextColumn)) return;

            Step(ref agent, column, height, (Grid.GetCellCenter(nextColumn, 0f) - agent.Position).xz, speed);
        }

        /// <summary>
        /// 水平の位置 target へ向かう。着いたら arriveYaw を向いて止まる。
        /// まっすぐ進めなければ target に近づく隣の列へ、それもなければ距離マップの値が下がる列へ進む。
        /// </summary>
        private void MoveToward(ref EnemyAgent agent, int column, float height, float distance, float2 target, float arriveYaw,
            float speed)
        {
            var toTarget = target - agent.Position.xz;
            var targetDistance = math.length(toTarget);
            if (targetDistance < ARRIVE_DISTANCE)
            {
                Turn(ref agent, arriveYaw);
                return;
            }

            speed *= math.saturate(targetDistance / SLOW_DOWN_DISTANCE);
            var probe = agent.Position.xz + toTarget / targetDistance * Grid.CellSize;
            if (!Grid.IsBlocked(column, height, probe))
            {
                Step(ref agent, column, height, toTarget, speed);
                return;
            }

            if (Grid.TryGetColumnToward(column, height, target, out var towardColumn)
                || (!float.IsPositiveInfinity(distance)
                    && Grid.TryGetDownhillColumn(column, height, distance, Distances, out towardColumn)))
            {
                Step(ref agent, column, height, (Grid.GetCellCenter(towardColumn, 0f) - agent.Position).xz, speed);
            }
        }

        /// <summary>
        /// 向きを toNext の方へ TurnSpeed を上限に回し、向いている方へ進む。進む先から外れている間は、そのずれの分だけ遅くなる。
        /// 進んだ先の列に乗れなければ進まない。
        /// </summary>
        private void Step(ref EnemyAgent agent, int column, float height, float2 toNext, float speed)
        {
            Turn(ref agent, math.atan2(toNext.x, toNext.y));

            var forward = new float2(math.sin(agent.Yaw), math.cos(agent.Yaw));
            var alignment = math.saturate(math.dot(forward, math.normalizesafe(toNext)));
            var next = agent.Position + new float3(forward.x, 0f, forward.y) * (speed * alignment * DeltaTime);

            if (!Grid.TryGetColumn(next, out var movedColumn)) return;
            if (movedColumn != column && Grid.GetLandingNode(movedColumn, height) < 0) return;

            agent.Position.x = next.x;
            agent.Position.z = next.z;
        }

        private void Turn(ref EnemyAgent agent, float desiredYaw)
        {
            var yawDelta = math.atan2(math.sin(desiredYaw - agent.Yaw), math.cos(desiredYaw - agent.Yaw));
            agent.Yaw += math.clamp(yawDelta, -TurnSpeed * DeltaTime, TurnSpeed * DeltaTime);
        }

        /// <summary>
        /// グループに入っている敵の、隊列の位置（水平）と、その位置での隊列の向きを返す。グループを持たなければ false。
        /// 隊列の位置は、道筋に沿ってアンカーから (列の番号 × 列の間隔) 後ろの点を、列の中の位置に応じて道筋の横へずらした点。
        /// 置き場に着いたグループでは、グループの中心（アンカー）を中心にした螺旋の上の、隊列の順番の位置にし、プレイヤーを向く。
        /// 螺旋の 0 番はグループの中心で、螺旋は 0 番から外側へ向かう向きがプレイヤーへの向きになるよう回す。
        /// 螺旋の上の位置が壁の向こうや穴の上に来たら、中心からその向きへ同じ高さの床が続く所まで縮める。
        /// 移動中は、道筋に沿って並ぶ（曲がり角で壁に詰まらない為）。
        /// </summary>
        private bool TryGetSlotTarget(in EnemyAgent agent, out float2 target, out float yaw)
        {
            target = default;
            yaw = 0f;
            if (agent.GroupIndex < 0) return false;

            var group = Groups[agent.GroupIndex];
            if (!group.IsActive) return false;

            if (group.HasArrived)
            {
                var toPlayer = math.normalizesafe((PlayerPosition - group.AnchorPosition).xz, new float2(0f, 1f));
                var offset = EnemyFormationSettings.GetSpiralOffset(agent.SlotIndex, 0f, Formation.MemberLoopSpacing,
                    Formation.MemberSlotSpacing);
                var side = new float2(toPlayer.y, -toPlayer.x);
                var fromCenter = side * offset.x + toPlayer * offset.y;
                var length = math.length(fromCenter);
                var direction = length > 0f ? fromCenter / length : float2.zero;
                target = group.AnchorPosition.xz + direction * Grid.GetFlatFloorLength(group.AnchorPosition, direction, length);
                yaw = math.atan2((PlayerPosition.xz - target).x, (PlayerPosition.xz - target).y);
                return true;
            }

            var columnCount = math.max(1, group.ColumnCount);
            var row = agent.SlotIndex / columnCount;
            var lane = agent.SlotIndex % columnCount;
            var lanesInRow = math.min(columnCount, group.MemberCount - row * columnCount);
            var lateral = (lane - (lanesInRow - 1) * 0.5f) * Formation.LateralSpacing;
            var back = row * Formation.RowSpacing;

            EnemyGroups.SamplePath(Paths, agent.GroupIndex, group, back, out var point, out var tangent);

            var right = new float2(tangent.y, -tangent.x);
            target = point.xz + right * ClampLateral(point, right, lateral);
            yaw = math.atan2(tangent.x, tangent.y);
            return true;
        }

        /// <summary>
        /// 道筋の点 point から右向き right へ lateral（m、負なら左）ずらすとき、point と同じ高さの床が続く分だけに縮めた値を返す。
        /// </summary>
        private float ClampLateral(float3 point, float2 right, float lateral)
        {
            var side = math.sign(lateral);
            return side * Grid.GetFlatFloorLength(point, right * side, math.abs(lateral));
        }

        /// <summary>
        /// FlyTarget へまっすぐ飛ぶ。飛ぶ先の近くでは遅くなり、飛ぶ先を越えない。向きは変えない。
        /// </summary>
        private void Fly(ref EnemyAgent agent)
        {
            var toTarget = agent.FlyTarget - agent.Position;
            var distance = math.length(toTarget);
            if (distance <= 0f) return;

            var speed = math.max(FlySpeed * math.saturate(distance / FLY_SLOW_DOWN_DISTANCE), MIN_FLY_SPEED);
            agent.Position += toTarget / distance * math.min(speed * DeltaTime, distance);
        }

        /// <summary>
        /// 立っている敵は床の高さに合わせ、床が下がりすぎていれば落とす。落ちている敵は重力で落とし、床に着いたら立たせる。
        /// 真下の列に着地できる層がなければ、周りの列のうち最も近い列の層に着地し、位置をその列の中へずらす。
        /// 橋の下のように頭上が背丈より低い所は立てる層にならないので、真下だけを見ると地面を抜けて落ち続ける為。
        /// 落ち始めた高さから一定以上落ちて着地した敵と、格子の範囲より下まで落ちた敵は、崩落で倒されたとしてステージから消す。
        /// 敵が自分で降りるのは降りられる高さまでなので、それより高く落ちるのは足場が壊れたときになる。
        /// </summary>
        private void UpdateVertical(ref EnemyAgent agent)
        {
            var hasColumn = Grid.TryGetColumn(agent.Position, out var column);

            if (agent.IsGrounded)
            {
                var floor = hasColumn ? Grid.GetLandingNode(column, agent.Position.y) : -1;
                if (floor >= 0 && Grid.Heights[floor] >= agent.Position.y - STEP_DOWN_HEIGHT)
                {
                    agent.Position.y = Grid.Heights[floor];
                    return;
                }

                agent.IsGrounded = false;
                agent.VerticalSpeed = 0f;
                agent.FallStartHeight = agent.Position.y;
            }

            var previousY = agent.Position.y;
            agent.VerticalSpeed -= Gravity * DeltaTime;
            agent.Position.y += agent.VerticalSpeed * DeltaTime;

            var landing = -1;
            var landingColumn = column;
            if (hasColumn)
            {
                landing = Grid.GetHighestNodeBelow(column, previousY + GROUND_TOLERANCE);
                if (landing < 0) landing = FindNearbyLanding(column, previousY + GROUND_TOLERANCE, out landingColumn);
            }

            if (landing >= 0 && agent.Position.y <= Grid.Heights[landing])
            {
                if (landingColumn != column) MoveIntoColumn(ref agent.Position, landingColumn);

                agent.Position.y = Grid.Heights[landing];
                agent.VerticalSpeed = 0f;
                agent.IsGrounded = true;
                if (agent.FallStartHeight - agent.Position.y >= FallDefeatHeight) DefeatByCollapse(ref agent);
                return;
            }

            if (agent.Position.y < Grid.Origin.y) DefeatByCollapse(ref agent);
        }

        /// <summary>
        /// 列 column の周り LANDING_SEARCH_RADIUS 列までを近い順に調べ、最初に見つかった周の中で、高さ maxHeight 以下で最も高い層を返す。なければ -1。
        /// </summary>
        private int FindNearbyLanding(int column, float maxHeight, out int landingColumn)
        {
            landingColumn = -1;
            var x = column % Grid.Width;
            var z = column / Grid.Width;

            for (var radius = 1; radius <= LANDING_SEARCH_RADIUS; radius++)
            {
                var best = -1;
                for (var dz = -radius; dz <= radius; dz++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != radius) continue;

                        var nx = x + dx;
                        var nz = z + dz;
                        if (nx < 0 || nx >= Grid.Width || nz < 0 || nz >= Grid.Depth) continue;

                        var candidateColumn = nz * Grid.Width + nx;
                        var node = Grid.GetHighestNodeBelow(candidateColumn, maxHeight);
                        if (node < 0 || (best >= 0 && Grid.Heights[node] <= Grid.Heights[best])) continue;

                        best = node;
                        landingColumn = candidateColumn;
                    }
                }

                if (best >= 0) return best;
            }

            return -1;
        }

        /// <summary>
        /// 水平の位置を、列 column のマスの中の最も近い点へ移す。
        /// </summary>
        private void MoveIntoColumn(ref float3 position, int column)
        {
            var cellMin = Grid.Origin.xz + new float2(column % Grid.Width, column / Grid.Width) * Grid.CellSize;
            var inside = math.clamp(position.xz, cellMin + COLUMN_EDGE_MARGIN, cellMin + Grid.CellSize - COLUMN_EDGE_MARGIN);
            position.x = inside.x;
            position.z = inside.y;
        }

        public void Execute(int index)
        {
            var agent = Agents[index];
            if (!agent.IsAlive) return;

            if (agent.MoveMode == EnemyMoveMode.Flying)
            {
                Fly(ref agent);
                Agents[index] = agent;
                return;
            }

            // 壊れた移動部位が上限に達した敵と、止められた敵は歩かない。足場がなくなれば落ちる
            if (agent.IsGrounded && agent.MoveMode == EnemyMoveMode.Walking &&
                agent.BrokenMovePartCount < agent.BrokenMovePartLimit) Walk(ref agent, index);
            UpdateVertical(ref agent);
            UpdateStrandedTime(ref agent);

            Agents[index] = agent;
        }
    }
}
