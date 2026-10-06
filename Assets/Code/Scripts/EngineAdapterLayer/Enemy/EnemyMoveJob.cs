using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 出ている敵を 1 体ずつ動かす。立っている敵は距離マップの値が下がる隣の列へ向かって歩き、立てる層がなくなると落ちる。
    /// 自分の番号の敵だけを書き換え、ほかの敵は見ない。
    /// </summary>
    /// <remarks>
    /// 進む先は、隣の 8 列のうち、移動の規則（EnemyNavigationGrid）で乗る層の距離が最も小さい列の中心。
    /// 向きは進む先へ回る速さの上限つきで回し、向いている方へ進む。進む先から外れている間は、そのずれの分だけ遅くなる。
    /// 進んだ先の列に乗る層がなければ（壁や、降りられる高さを超える崖）、そのフレームは進まない。
    /// 段差は、登れる高さまでならその場で乗り、少しの下りは床に合わせ、それより低ければ落ちる。
    /// 壊れた移動部位が上限に達した敵は歩かないが、足場がなくなれば落ちる。
    /// </remarks>
    // TODO: 区間4C で、グループと隊列、移動部位を失って止まった敵の隊列での扱いを入れる
    [BurstCompile]
    public struct EnemyMoveJob : IJobParallelFor
    {
        /// <summary> 立ったまま床に合わせて下りる高さ（m）。これより低い床へは落ちる </summary>
        private const float STEP_DOWN_HEIGHT = 0.5f;

        /// <summary> 立っている層とみなす、位置より上の高さ（m） </summary>
        private const float GROUND_TOLERANCE = 0.05f;

        public NativeArray<EnemyAgent> Agents;
        public EnemyNavigationGrid Grid;
        [ReadOnly] public NativeArray<float> Distances;
        public float DeltaTime;

        /// <summary> 歩く速さ（m/s） </summary>
        public float MoveSpeed;

        /// <summary> 向きを変える速さ（ラジアン/秒） </summary>
        public float TurnSpeed;

        /// <summary> 距離マップの値がこれ以下（m）なら止まる </summary>
        public float StopDistance;

        /// <summary> 重力の加速度の大きさ（m/s²） </summary>
        public float Gravity;

        /// <summary> 壊れた移動部位がこの数に達した敵は歩かない </summary>
        public int BrokenMovePartLimit;

        public void Execute(int index)
        {
            var agent = Agents[index];
            if (!agent.IsAlive) return;

            if (agent.IsGrounded && agent.BrokenMovePartCount < BrokenMovePartLimit) Walk(ref agent);
            UpdateVertical(ref agent);

            Agents[index] = agent;
        }

        private void Walk(ref EnemyAgent agent)
        {
            if (!Grid.TryGetColumn(agent.Position, out var column)) return;

            var node = Grid.GetHighestNodeBelow(column, agent.Position.y + GROUND_TOLERANCE);
            if (node < 0) return;

            var distance = Distances[node];
            if (distance <= StopDistance || float.IsPositiveInfinity(distance)) return;
            if (!TryGetNextColumn(column, Grid.Heights[node], distance, out var nextColumn)) return;

            var toNext = (Grid.GetCellCenter(nextColumn, 0f) - agent.Position).xz;
            var desiredYaw = math.atan2(toNext.x, toNext.y);
            var yawDelta = math.atan2(math.sin(desiredYaw - agent.Yaw), math.cos(desiredYaw - agent.Yaw));
            agent.Yaw += math.clamp(yawDelta, -TurnSpeed * DeltaTime, TurnSpeed * DeltaTime);

            var forward = new float2(math.sin(agent.Yaw), math.cos(agent.Yaw));
            var alignment = math.saturate(math.dot(forward, math.normalizesafe(toNext)));
            var next = agent.Position + new float3(forward.x, 0f, forward.y) * (MoveSpeed * alignment * DeltaTime);

            if (!Grid.TryGetColumn(next, out var movedColumn)) return;
            if (movedColumn != column && Grid.GetLandingNode(movedColumn, Grid.Heights[node]) < 0) return;

            agent.Position.x = next.x;
            agent.Position.z = next.z;
        }

        /// <summary>
        /// 隣の 8 列のうち、高さ height から進んで乗る層の距離が、今の距離 distance より小さく、最も小さい列を返す。
        /// </summary>
        private bool TryGetNextColumn(int column, float height, float distance, out int nextColumn)
        {
            nextColumn = -1;
            var bestDistance = distance;
            var x = column % Grid.Width;
            var z = column / Grid.Width;

            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;

                    var nx = x + dx;
                    var nz = z + dz;
                    if (nx < 0 || nx >= Grid.Width || nz < 0 || nz >= Grid.Depth) continue;

                    var candidate = nz * Grid.Width + nx;
                    var landing = Grid.GetLandingNode(candidate, height);
                    if (landing < 0 || Distances[landing] >= bestDistance) continue;

                    if (dx != 0 && dz != 0
                        && !(Grid.CanCross(z * Grid.Width + nx, height) && Grid.CanCross(nz * Grid.Width + x, height))) continue;

                    bestDistance = Distances[landing];
                    nextColumn = candidate;
                }
            }

            return nextColumn >= 0;
        }

        /// <summary>
        /// 立っている敵は床の高さに合わせ、床が下がりすぎていれば落とす。落ちている敵は重力で落とし、床に着いたら立たせる。
        /// 格子の範囲より下まで落ちた敵は、ステージから消す。
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
            }

            var previousY = agent.Position.y;
            agent.VerticalSpeed -= Gravity * DeltaTime;
            agent.Position.y += agent.VerticalSpeed * DeltaTime;

            var landing = hasColumn ? Grid.GetHighestNodeBelow(column, previousY + GROUND_TOLERANCE) : -1;
            if (landing >= 0 && agent.Position.y <= Grid.Heights[landing])
            {
                agent.Position.y = Grid.Heights[landing];
                agent.VerticalSpeed = 0f;
                agent.IsGrounded = true;
                return;
            }

            if (agent.Position.y < Grid.Origin.y) agent.IsAlive = false;
        }
    }
}
