using System;
using Kizami.BlackBoard;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループ（EnemyGroup）と、その道筋・帰りの道筋・メンバーの配列を持つ。
    /// EnemySpawnAdapter が、生成した敵を順にグループへ入れ、毎フレーム EnemyGroupJob を回し、グループを 1 つずつ並べ替える。
    /// </summary>
    public sealed class EnemyGroups : IDisposable
    {
        /// <summary> グループごとの道筋の点の数。1 列の縦隊の最後尾（人数 × 列の間隔）まで届く長さにする </summary>
        public const int PATH_CAPACITY = 128;

        /// <summary> 道筋に点を足す間隔（m） </summary>
        public const float PATH_SPACING = 1f;

        /// <summary> グループごとの帰りの道筋の点の数。追跡範囲（100m の区画で 300m 四方）を往復できる長さにする </summary>
        public const int RETURN_PATH_CAPACITY = 64;

        /// <summary> 帰りの道筋に点を足す間隔（m） </summary>
        public const float RETURN_PATH_SPACING = 5f;

        private NativeArray<EnemyGroup> _groups;

        /// <summary> グループごとに PATH_CAPACITY 個の区画を持つ道筋の点。区画はリングバッファとして使う </summary>
        private NativeArray<float3> _paths;

        /// <summary> グループごとに RETURN_PATH_CAPACITY 個の区画を持つ帰りの道筋の点。区画はリングバッファのスタックとして使い、あふれたら古い点を捨てる </summary>
        private NativeArray<float3> _returnPaths;

        /// <summary> グループごとに EnemyFormationSettings.MAX_GROUP_SIZE 個の区画を持つ、メンバーの敵の番号 </summary>
        private NativeArray<int> _members;

        /// <summary> 生成した敵を入れていくグループ。次に入れる敵で新しいグループを作るなら -1 </summary>
        private int _openGroup = -1;

        /// <summary> 次に整えるグループを探し始める番号 </summary>
        private int _maintainCursor;

        /// <summary> グループの状態。EnemyMoveJob が読む </summary>
        public NativeArray<EnemyGroup> Groups => _groups;

        /// <summary> 道筋の点。EnemyMoveJob が読む </summary>
        public NativeArray<float3> Paths => _paths;

        /// <summary> 使われているグループの数 </summary>
        public int ActiveCount
        {
            get
            {
                var count = 0;
                foreach (var group in _groups)
                {
                    if (group.IsActive) count++;
                }

                return count;
            }
        }

        /// <param name="capacity">敵の状態の数。1 体ずつ出した敵がそれぞれ別のグループになっても足りるよう、グループの数の上限も同じ数にする</param>
        public EnemyGroups(int capacity)
        {
            _groups = new NativeArray<EnemyGroup>(capacity, Allocator.Persistent);
            _paths = new NativeArray<float3>(capacity * PATH_CAPACITY, Allocator.Persistent);
            _returnPaths = new NativeArray<float3>(capacity * RETURN_PATH_CAPACITY, Allocator.Persistent);
            _members = new NativeArray<int>(capacity * EnemyFormationSettings.MAX_GROUP_SIZE, Allocator.Persistent);
        }

        /// <summary>
        /// グループの道筋を、アンカーから back（m）だけ後ろへたどった点と、そこでの進む向き（水平の単位ベクトル）を返す。
        /// 道筋が足りなければ、最も古い点を返す。
        /// </summary>
        /// <param name="paths">グループごとに PATH_CAPACITY 個の区画を持つ道筋の点</param>
        public static void SamplePath(NativeArray<float3> paths, int groupIndex, in EnemyGroup group, float back,
            out float3 point, out float2 tangent)
        {
            var offset = groupIndex * PATH_CAPACITY;
            var current = group.AnchorPosition;
            tangent = new float2(math.sin(group.AnchorYaw), math.cos(group.AnchorYaw));
            var remaining = back;

            for (var k = 0; k < group.PathCount; k++)
            {
                var older = paths[offset + (group.PathHead - k + PATH_CAPACITY) % PATH_CAPACITY];
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
        /// グループ g の隊列の 0 番の敵の番号を返す。メンバーがいなければ -1。
        /// 倒れた敵やほかのグループへ移った敵が、次に整えるまで残っていることがあるので、呼び出し元で生きているかとグループの番号を確かめる。
        /// </summary>
        public int GetLeader(int g)
        {
            return _groups[g].MemberCount > 0 ? _members[g * EnemyFormationSettings.MAX_GROUP_SIZE] : -1;
        }

        /// <summary>
        /// 敵をグループから抜く。抜いた敵の区画は、次にそのグループを整えるときに詰める。
        /// </summary>
        public static void Leave(ref EnemyAgent agent)
        {
            agent.GroupIndex = -1;
        }

        /// <summary>
        /// 使われているグループを、状態ごとに数える。
        /// </summary>
        public void CountStates(out int waiting, out int tracking, out int returning)
        {
            waiting = 0;
            tracking = 0;
            returning = 0;
            foreach (var group in _groups)
            {
                if (!group.IsActive) continue;

                switch (group.State)
                {
                    case EnemyGroupState.Waiting:
                        waiting++;
                        break;
                    case EnemyGroupState.Tracking:
                        tracking++;
                        break;
                    case EnemyGroupState.Returning:
                        returning++;
                        break;
                }
            }
        }

        /// <summary>
        /// 次に入れる敵から、新しいグループにする。
        /// </summary>
        public void CloseGroup()
        {
            _openGroup = -1;
        }

        /// <summary>
        /// 敵を、生成中のグループの隊列の最後に入れる。グループがないか人数に達していれば、新しいグループを作る。
        /// 空いているグループがなければ入れず false を返す。グループの数は有効なスポーン位置の数で、スポーン位置ごとに CloseGroup するので、生成のときには空きがある。
        /// </summary>
        /// <param name="agentIndex">敵の状態の番号</param>
        /// <param name="agent">入れる敵。GroupIndex と SlotIndex を書く</param>
        /// <param name="anchorPosition">新しいグループを作るときの、アンカーの位置。持ち場にもする</param>
        /// <param name="anchorYaw">新しいグループを作るときの、アンカーの向き</param>
        /// <param name="formation">隊列の設定</param>
        public bool TryAdd(int agentIndex, ref EnemyAgent agent, float3 anchorPosition, float anchorYaw,
            in EnemyFormationSettings formation)
        {
            if (_openGroup < 0 || !_groups[_openGroup].IsActive || _groups[_openGroup].MemberCount >= EnemyFormationSettings.MAX_GROUP_SIZE)
            {
                _openGroup = OpenGroup(anchorPosition, anchorYaw, formation);
                if (_openGroup < 0) return false;
            }

            var group = _groups[_openGroup];
            _members[_openGroup * EnemyFormationSettings.MAX_GROUP_SIZE + group.MemberCount] = agentIndex;
            agent.GroupIndex = _openGroup;
            agent.SlotIndex = group.MemberCount;
            group.MemberCount++;
            _groups[_openGroup] = group;
            return true;
        }

        /// <summary>
        /// グループを 1 つずつ更新する Job を回す。
        /// </summary>
        /// <param name="trackingMin">distances を計算した追跡範囲の、最小の列 (x, z)</param>
        /// <param name="trackingMax">distances を計算した追跡範囲の、最大の列 (x, z)。この列も含む</param>
        public JobHandle Schedule(NativeArray<EnemyAgent> agents, EnemyNavigationGrid grid, NativeArray<float> distances,
            int2 trackingMin, int2 trackingMax, in EnemyFormationSettings formation, float3 playerPosition, float moveSpeed,
            float deltaTime)
        {
            return new EnemyGroupJob
            {
                Groups = _groups,
                Paths = _paths,
                ReturnPaths = _returnPaths,
                Members = _members,
                Agents = agents,
                Grid = grid,
                Distances = distances,
                TrackingMin = trackingMin,
                TrackingMax = trackingMax,
                Formation = formation,
                PlayerPosition = playerPosition,
                MoveSpeed = moveSpeed,
                DeltaTime = deltaTime
            }.Schedule();
        }

        /// <summary>
        /// 使われているグループを 1 つ順番に選んで整える。毎フレーム 1 グループずつ呼ぶ。Job が走っていない間に呼ぶ。
        /// 倒れた敵、動けなくなった敵を隊列から抜いて順番を詰め（穴を後ろへずらす）、
        /// メンバーの隊列の順番を、向かう先に近い順に並べ替える（先頭の列に向かう先に近いメンバーが来て、隊列の位置へ向かうメンバーどうしが交差しにくくなる）。
        /// 向かう先に近い順は、帰還中は持ち場までの直線の距離、それ以外はプレイヤーまでの経路の長さで決める。
        /// 置き場に着いて螺旋に並んでいるグループは並べ替えない。順番が螺旋の上の位置なので、入れ替えると並び直しが続く為。
        /// </summary>
        public void MaintainNext(NativeArray<EnemyAgent> agents, EnemyNavigationGrid grid, NativeArray<float> distances)
        {
            for (var attempt = 0; attempt < _groups.Length; attempt++)
            {
                var g = _maintainCursor;
                _maintainCursor = (_maintainCursor + 1) % _groups.Length;
                if (!_groups[g].IsActive) continue;

                Compact(g, agents);
                if (!_groups[g].IsActive) return;

                if (!_groups[g].HasArrived) Reorder(g, agents, grid, distances);
                return;
            }
        }

        /// <summary>
        /// 倒れた敵、動けなくなった敵を区画から抜き、残りを順番を保って前へ詰める。
        /// 動けなくなった敵（壊れた移動部位が上限に達した敵）はグループから抜いて、その場に残す。残りがいなければグループを空ける。
        /// ディフェンダーがいれば、最初の 1 体を 0 番（着いたら螺旋の中心）へ移す。戻れない敵の移し替えで後ろに入っても、ここで中心に戻る。
        /// </summary>
        private void Compact(int g, NativeArray<EnemyAgent> agents)
        {
            var offset = g * EnemyFormationSettings.MAX_GROUP_SIZE;
            var group = _groups[g];
            var count = 0;
            var defender = -1;

            for (var i = 0; i < group.MemberCount; i++)
            {
                var index = _members[offset + i];
                var agent = agents[index];
                if (!agent.IsAlive || agent.GroupIndex != g) continue;

                if (agent.BrokenMovePartCount >= agent.BrokenMovePartLimit)
                {
                    Leave(ref agent);
                    agents[index] = agent;
                    continue;
                }

                if (defender < 0 && agent.Kind == EnemyKind.Defender) defender = count;
                _members[offset + count] = index;
                count++;
            }

            if (defender > 0)
            {
                var index = _members[offset + defender];
                for (var i = defender; i > 0; i--) _members[offset + i] = _members[offset + i - 1];
                _members[offset] = index;
            }

            for (var i = 0; i < count; i++)
            {
                var index = _members[offset + i];
                var agent = agents[index];
                agent.SlotIndex = i;
                agents[index] = agent;
            }

            group.MemberCount = count;
            group.IsActive = count > 0;
            _groups[g] = group;
        }

        private void Reorder(int g, NativeArray<EnemyAgent> agents, EnemyNavigationGrid grid, NativeArray<float> distances)
        {
            var offset = g * EnemyFormationSettings.MAX_GROUP_SIZE;
            var group = _groups[g];
            var count = group.MemberCount;
            var isReturning = group.State == EnemyGroupState.Returning;
            Span<float> keys = stackalloc float[EnemyFormationSettings.MAX_GROUP_SIZE];

            for (var i = 0; i < count; i++)
            {
                var agent = agents[_members[offset + i]];
                keys[i] = float.MaxValue;
                if (!agent.IsAlive || agent.GroupIndex != g) continue;

                // ディフェンダーは並べ替えずに先頭（0 番）に置く
                if (agent.Kind == EnemyKind.Defender)
                {
                    keys[i] = float.MinValue;
                    continue;
                }

                if (isReturning)
                {
                    keys[i] = math.distancesq(agent.Position.xz, group.HomePosition.xz);
                    continue;
                }

                if (!grid.TryGetColumn(agent.Position, out var column)) continue;

                var node = grid.GetHighestNodeBelow(column, agent.Position.y + grid.ClimbHeight);
                if (node >= 0) keys[i] = distances[node];
            }

            for (var i = 1; i < count; i++)
            {
                var key = keys[i];
                var member = _members[offset + i];
                var j = i - 1;
                while (j >= 0 && keys[j] > key)
                {
                    keys[j + 1] = keys[j];
                    _members[offset + j + 1] = _members[offset + j];
                    j--;
                }

                keys[j + 1] = key;
                _members[offset + j + 1] = member;
            }

            for (var i = 0; i < count; i++)
            {
                var index = _members[offset + i];
                var agent = agents[index];
                if (!agent.IsAlive || agent.GroupIndex != g) continue;

                agent.SlotIndex = i;
                agents[index] = agent;
            }
        }

        /// <summary>
        /// 空いているグループを使い始め、道筋をアンカーの後ろへまっすぐ伸ばした点で埋めて、生成した直後から隊列の位置が決まるようにする。空きがなければ -1。
        /// アンカーの位置を持ち場にし、待機の状態から始める。
        /// </summary>
        private int OpenGroup(float3 anchorPosition, float anchorYaw, in EnemyFormationSettings formation)
        {
            for (var g = 0; g < _groups.Length; g++)
            {
                if (_groups[g].IsActive) continue;

                var backward = -new float3(math.sin(anchorYaw), 0f, math.cos(anchorYaw));
                var pathCount = math.min(PATH_CAPACITY,
                    (int)math.ceil(EnemyFormationSettings.MAX_GROUP_SIZE * formation.RowSpacing / PATH_SPACING) + 1);
                for (var i = 0; i < pathCount; i++)
                {
                    // 最も古い点が最も後ろになるよう、区画の先頭から後ろの点を並べる
                    _paths[g * PATH_CAPACITY + i] = anchorPosition + backward * ((pathCount - 1 - i) * PATH_SPACING);
                }

                _groups[g] = new EnemyGroup
                {
                    IsActive = true,
                    State = EnemyGroupState.Waiting,
                    HomePosition = anchorPosition,
                    AnchorPosition = anchorPosition,
                    AnchorYaw = anchorYaw,
                    AnchorDistance = float.PositiveInfinity,
                    EncircleSlot = -1,
                    ColumnCount = 1,
                    PendingColumnCount = 1,
                    PathHead = pathCount - 1,
                    PathCount = pathCount
                };
                return g;
            }

            return -1;
        }

        public void Dispose()
        {
            if (_groups.IsCreated) _groups.Dispose();
            if (_paths.IsCreated) _paths.Dispose();
            if (_returnPaths.IsCreated) _returnPaths.Dispose();
            if (_members.IsCreated) _members.Dispose();
        }
    }
}
