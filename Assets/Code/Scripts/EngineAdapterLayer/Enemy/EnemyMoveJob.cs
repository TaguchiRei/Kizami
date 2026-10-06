using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 出ている敵を 1 体ずつ動かす。交戦中の敵はプレイヤーの周りの置き場へ、グループに入っている敵は隊列の位置へ、
    /// グループを持たない敵は距離マップの値が下がる隣の列へ向かって歩き、立てる層がなくなると落ちる。
    /// 自分の番号の敵だけを書き換え、ほかの敵は見ない。
    /// </summary>
    /// <remarks>
    /// プレイヤーまでの経路の長さが交戦に入る距離以下になった敵は隊列から外れ、抜ける距離を超えたら隊列に戻る。
    /// 交戦中の敵の置き場は EnemyGroupJob が割り当て、プレイヤーと一緒に動く。置き場をまだ持たない間は、その場でプレイヤーを向いて待つ。
    /// 隊列の位置は、グループの道筋に沿ってアンカーから (列の番号 × 列の間隔) 後ろの点から、道筋の右へ (列の中の位置 × 横の間隔) ずらした点。
    /// アンカーが包囲の置き場を持つ間は、道筋ではなくアンカーの向きのまっすぐ後ろに並べる（着いたアンカーはプレイヤーを向くので、横隊がプレイヤーを向く）。
    /// 横へずらす途中でその点と同じ高さの床が途切れたら、その手前で止める（通路では細くなる）。
    /// 目指す位置へまっすぐ進めない（隣の列に乗れない）ときは、目指す位置に近づく隣の列へ、それもなければ距離マップの値が下がる列へ進む。
    /// 向きは進む先へ回る速さの上限つきで回し、向いている方へ進む。進む先から外れている間は、そのずれの分だけ遅くなる。
    /// 隊列の位置に着いたら隊列の向きを、交戦の置き場に着いたらプレイヤーを向く。
    /// 歩く速さは、敵ごとに ±10% ずらす（全員が同じ速さで動いて見えないようにする為）。
    /// 進んだ先の列に乗る層がなければ（壁や、降りられる高さを超える崖）、そのフレームは進まない。
    /// 段差は、登れる高さまでならその場で乗り、少しの下りは床に合わせ、それより低ければ落ちる。
    /// 壊れた移動部位が上限に達した敵は歩かないが、足場がなくなれば落ちる。
    /// </remarks>
    // TODO: 区間4C で、移動部位を失って止まった敵の隊列での扱いを入れる
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

        /// <summary> 歩く速さを敵ごとにずらす割合の幅（±） </summary>
        private const float SPEED_JITTER = 0.1f;

        public NativeArray<EnemyAgent> Agents;
        public EnemyNavigationGrid Grid;
        [ReadOnly] public NativeArray<float> Distances;

        /// <summary> グループの状態 </summary>
        [ReadOnly] public NativeArray<EnemyGroup> Groups;

        /// <summary> グループごとに EnemyGroups.PATH_CAPACITY 個の区画を持つ道筋の点 </summary>
        [ReadOnly] public NativeArray<float3> Paths;

        /// <summary> 敵ごとの、交戦する敵の置き場の番号。持たなければ -1 </summary>
        [ReadOnly] public NativeArray<int> EngageSlots;

        public EnemyFormationSettings Formation;

        /// <summary> プレイヤーの位置。交戦の置き場の中心 </summary>
        public float3 PlayerPosition;

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

        /// <summary>
        /// 敵の番号から決まる 0〜1 の値。
        /// </summary>
        private static float GetAgentRandom(int index)
        {
            return (math.hash(new uint2((uint)index, 0x85EBCA6Bu)) & 0xFFFFu) / 65535f;
        }

        public void Execute(int index)
        {
            var agent = Agents[index];
            if (!agent.IsAlive) return;

            if (agent.IsGrounded && agent.BrokenMovePartCount < BrokenMovePartLimit) Walk(ref agent, index);
            UpdateVertical(ref agent);

            Agents[index] = agent;
        }

        private void Walk(ref EnemyAgent agent, int index)
        {
            if (!Grid.TryGetColumn(agent.Position, out var column)) return;

            var node = Grid.GetHighestNodeBelow(column, agent.Position.y + GROUND_TOLERANCE);
            if (node < 0) return;

            var height = Grid.Heights[node];
            var distance = Distances[node];
            UpdateEngagement(ref agent, distance);

            var toPlayer = (PlayerPosition - agent.Position).xz;
            var yawToPlayer = math.atan2(toPlayer.x, toPlayer.y);
            if (distance <= StopDistance)
            {
                Turn(ref agent, yawToPlayer);
                return;
            }

            var speed = MoveSpeed * (1f + SPEED_JITTER * (2f * GetAgentRandom(index) - 1f));

            if (agent.IsEngaged)
            {
                var slot = EngageSlots[index];
                if (slot < 0)
                {
                    Turn(ref agent, yawToPlayer);
                    return;
                }

                var spiralPoint = PlayerPosition.xz + EnemyFormationSettings.GetSpiralOffset(slot, Formation.SpiralInnerRadius,
                    Formation.SpiralLoopSpacing, Formation.SpiralSlotSpacing);
                MoveToward(ref agent, column, height, distance, spiralPoint, yawToPlayer, speed);
                return;
            }

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
        /// プレイヤーまでの経路の長さが交戦に入る距離以下なら交戦に入り、抜ける距離を超えたら（たどり着けなくなったときも）抜ける。
        /// </summary>
        private void UpdateEngagement(ref EnemyAgent agent, float distance)
        {
            if (!agent.IsEngaged && distance <= Formation.EngageEnterDistance) agent.IsEngaged = true;
            else if (agent.IsEngaged && distance > Formation.EngageExitDistance) agent.IsEngaged = false;
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
            if (!IsBlocked(column, height, probe))
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
        /// 向きを toNext の方へ回し、向いている方へ進む。進んだ先の列に乗れなければ進まない。
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
        /// 高さ height で列 column にいる敵が、水平の位置 probe の列へ進めないか。同じ列なら進める。
        /// </summary>
        private bool IsBlocked(int column, float height, float2 probe)
        {
            if (!Grid.TryGetColumn(new float3(probe.x, height, probe.y), out var probeColumn)) return true;

            return probeColumn != column && Grid.GetLandingNode(probeColumn, height) < 0;
        }

        /// <summary>
        /// グループに入っている敵の、隊列の位置（水平）と、その位置での隊列の向きを返す。グループを持たなければ false。
        /// </summary>
        private bool TryGetSlotTarget(in EnemyAgent agent, out float2 target, out float yaw)
        {
            target = default;
            yaw = 0f;
            if (agent.GroupIndex < 0) return false;

            var group = Groups[agent.GroupIndex];
            if (!group.IsActive) return false;

            var columnCount = math.max(1, group.ColumnCount);
            var row = agent.SlotIndex / columnCount;
            var lane = agent.SlotIndex % columnCount;
            var lanesInRow = math.min(columnCount, group.MemberCount - row * columnCount);
            var lateral = (lane - (lanesInRow - 1) * 0.5f) * Formation.LateralSpacing;
            var back = row * Formation.RowSpacing;

            float3 point;
            float2 tangent;
            if (group.EncircleSlot >= 0)
            {
                tangent = new float2(math.sin(group.AnchorYaw), math.cos(group.AnchorYaw));
                point = group.AnchorPosition - new float3(tangent.x, 0f, tangent.y) * back;
            }
            else
            {
                SamplePath(agent.GroupIndex, group, back, out point, out tangent);
            }

            var right = new float2(tangent.y, -tangent.x);
            target = point.xz + right * ClampLateral(point, right, lateral);
            yaw = math.atan2(tangent.x, tangent.y);
            return true;
        }

        /// <summary>
        /// グループの道筋を、アンカーから back（m）だけ後ろへたどった点と、そこでの進む向き（水平の単位ベクトル）を返す。
        /// 道筋が足りなければ、最も古い点を返す。
        /// </summary>
        private void SamplePath(int groupIndex, in EnemyGroup group, float back, out float3 point, out float2 tangent)
        {
            var offset = groupIndex * EnemyGroups.PATH_CAPACITY;
            var current = group.AnchorPosition;
            tangent = new float2(math.sin(group.AnchorYaw), math.cos(group.AnchorYaw));
            var remaining = back;

            for (var k = 0; k < group.PathCount; k++)
            {
                var older = Paths[offset + (group.PathHead - k + EnemyGroups.PATH_CAPACITY) % EnemyGroups.PATH_CAPACITY];
                var segment = (current - older).xz;
                var length = math.length(segment);
                if (length < 1e-4f) continue;

                tangent = segment / length;
                if (length >= remaining)
                {
                    point = current - new float3(tangent.x, 0f, tangent.y) * remaining;
                    point.y = math.lerp(current.y, older.y, remaining / length);
                    return;
                }

                remaining -= length;
                current = older;
            }

            point = current;
        }

        /// <summary>
        /// 道筋の点 point から右向き right へ lateral（m、負なら左）ずらすとき、point と同じ高さの床が続く分だけに縮めた値を返す。
        /// </summary>
        private float ClampLateral(float3 point, float2 right, float lateral)
        {
            var side = math.sign(lateral);
            var length = math.abs(lateral);
            var steps = (int)math.ceil(length / Grid.CellSize);

            for (var s = 1; s <= steps; s++)
            {
                var stepLength = math.min(s * Grid.CellSize, length);
                var probe = point.xz + right * (side * stepLength);
                if (!Grid.TryGetColumn(new float3(probe.x, point.y, probe.y), out var column)
                    || Grid.GetNodeNear(column, point.y) < 0)
                {
                    return side * (s - 1) * Grid.CellSize;
                }
            }

            return lateral;
        }

        /// <summary>
        /// 立っている敵は床の高さに合わせ、床が下がりすぎていれば落とす。落ちている敵は重力で落とし、床に着いたら立たせる。
        /// 真下の列に着地できる層がなければ、周りの列のうち最も近い列の層に着地し、位置をその列の中へずらす。
        /// 橋の下のように頭上が背丈より低い所は立てる層にならないので、真下だけを見ると地面を抜けて落ち続ける為。
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
                return;
            }

            if (agent.Position.y < Grid.Origin.y) agent.IsAlive = false;
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
    }
}
