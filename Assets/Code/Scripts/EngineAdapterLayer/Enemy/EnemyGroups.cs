using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループ（EnemyGroup）と、その道筋とメンバーの配列、交戦する敵の置き場の配列を持つ。
    /// EnemySpawnAdapter が、生成した敵を順にグループへ入れ、毎フレーム EnemyGroupJob を回し、グループを 1 つずつ並べ替える。
    /// </summary>
    /// <remarks>
    /// グループの数は敵の状態の数と同じだけ用意する。1 体ずつ出した敵がそれぞれ別のグループになっても足りるようにする為。
    /// 新しいグループの道筋は、アンカーの後ろ（向きと反対側）へまっすぐ伸ばした点で埋め、生成した直後から隊列の位置が決まるようにする。
    /// </remarks>
    public sealed class EnemyGroups : IDisposable
    {
        /// <summary> グループごとの道筋の点の数。1 列の縦隊の最後尾（人数 × 列の間隔）まで届く長さにする </summary>
        public const int PATH_CAPACITY = 128;

        /// <summary> 道筋に点を足す間隔（m） </summary>
        public const float PATH_SPACING = 1f;

        /// <summary> 合流する先のグループの、アンカーどうしの距離の上限（m） </summary>
        private const float MERGE_DISTANCE = 40f;

        private NativeArray<EnemyGroup> _groups;

        /// <summary> グループごとに PATH_CAPACITY 個の区画を持つ道筋の点 </summary>
        private NativeArray<float3> _paths;

        /// <summary> グループごとに EnemyFormationSettings.MAX_GROUP_SIZE 個の区画を持つ、メンバーの敵の番号 </summary>
        private NativeArray<int> _members;

        /// <summary> 敵ごとの、交戦する敵の置き場の番号。持たなければ -1。EnemyGroupJob が割り当てる </summary>
        private NativeArray<int> _engageSlots;

        /// <summary> 生成した敵を入れていくグループ。次に入れる敵で新しいグループを作るなら -1 </summary>
        private int _openGroup = -1;

        /// <summary> 次に整えるグループを探し始める番号 </summary>
        private int _maintainCursor;

        /// <summary> グループの状態。EnemyMoveJob が読む </summary>
        public NativeArray<EnemyGroup> Groups => _groups;

        /// <summary> 道筋の点。EnemyMoveJob が読む </summary>
        public NativeArray<float3> Paths => _paths;

        /// <summary> 敵ごとの、交戦する敵の置き場の番号。EnemyMoveJob が読む </summary>
        public NativeArray<int> EngageSlots => _engageSlots;

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

        /// <param name="capacity">敵の状態の数。グループの数の上限も同じ数にする</param>
        public EnemyGroups(int capacity)
        {
            _groups = new NativeArray<EnemyGroup>(capacity, Allocator.Persistent);
            _paths = new NativeArray<float3>(capacity * PATH_CAPACITY, Allocator.Persistent);
            _members = new NativeArray<int>(capacity * EnemyFormationSettings.MAX_GROUP_SIZE, Allocator.Persistent);
            _engageSlots = new NativeArray<int>(capacity, Allocator.Persistent);
            for (var i = 0; i < capacity; i++) _engageSlots[i] = -1;
        }

        /// <summary>
        /// 敵をグループから抜く。抜いた敵の区画は、次にそのグループを整えるときに詰める。
        /// </summary>
        public static void Leave(ref EnemyAgent agent)
        {
            agent.GroupIndex = -1;
            agent.IsEngaged = false;
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
        /// 空いているグループがなければ入れず false を返し、敵はグループを持たないまま（GroupIndex = -1）にする。
        /// </summary>
        /// <param name="agentIndex">敵の状態の番号</param>
        /// <param name="agent">入れる敵。GroupIndex と SlotIndex を書く</param>
        /// <param name="anchorPosition">新しいグループを作るときの、アンカーの位置</param>
        /// <param name="anchorYaw">新しいグループを作るときの、アンカーの向き</param>
        /// <param name="phaseTimer">新しいグループを作るときの、最初に進み始めるまでの時間（秒）</param>
        /// <param name="formation">隊列の設定</param>
        public bool TryAdd(int agentIndex, ref EnemyAgent agent, float3 anchorPosition, float anchorYaw, float phaseTimer,
            in EnemyFormationSettings formation)
        {
            if (_openGroup < 0 || !_groups[_openGroup].IsActive || _groups[_openGroup].MemberCount >= formation.GroupSize)
            {
                _openGroup = OpenGroup(anchorPosition, anchorYaw, phaseTimer, formation);
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
        public JobHandle Schedule(NativeArray<EnemyAgent> agents, EnemyNavigationGrid grid, NativeArray<float> distances,
            in EnemyFormationSettings formation, float3 playerPosition, float moveSpeed, float deltaTime)
        {
            return new EnemyGroupJob
            {
                Groups = _groups,
                Paths = _paths,
                Members = _members,
                EngageSlots = _engageSlots,
                Agents = agents,
                Grid = grid,
                Distances = distances,
                Formation = formation,
                PlayerPosition = playerPosition,
                MoveSpeed = moveSpeed,
                DeltaTime = deltaTime
            }.Schedule();
        }

        /// <summary>
        /// 使われているグループを 1 つ順番に選んで整える。毎フレーム 1 グループずつ呼ぶ。Job が走っていない間に呼ぶ。
        /// 倒れた敵、ほかのグループへ移った敵、動けなくなった敵を隊列から抜いて順番を詰め（穴を後ろへずらす）、
        /// メンバーが合流する数以下に減っていれば、近くの空きのあるグループへ合流させる。
        /// 合流しなければ、メンバーの隊列の順番を、プレイヤーまでの経路が短い順に並べ替える（先頭の列にプレイヤーに近いメンバーが来て、隊列の位置へ向かうメンバーどうしが交差しにくくなる）。
        /// </summary>
        /// <param name="brokenMovePartLimit">壊れた移動部位がこの数に達した敵は動けないので、グループから抜いてその場に残す</param>
        public void MaintainNext(NativeArray<EnemyAgent> agents, EnemyNavigationGrid grid, NativeArray<float> distances,
            in EnemyFormationSettings formation, int brokenMovePartLimit)
        {
            for (var attempt = 0; attempt < _groups.Length; attempt++)
            {
                var g = _maintainCursor;
                _maintainCursor = (_maintainCursor + 1) % _groups.Length;
                if (!_groups[g].IsActive) continue;

                Compact(g, agents, brokenMovePartLimit);
                if (!_groups[g].IsActive) return;
                if (_groups[g].MemberCount <= formation.MergeSize && TryMerge(g, agents, formation)) return;

                Reorder(g, agents, grid, distances);
                return;
            }
        }

        public void Dispose()
        {
            if (_groups.IsCreated) _groups.Dispose();
            if (_paths.IsCreated) _paths.Dispose();
            if (_members.IsCreated) _members.Dispose();
            if (_engageSlots.IsCreated) _engageSlots.Dispose();
        }

        /// <summary>
        /// 倒れた敵、ほかのグループへ移った敵、動けなくなった敵を区画から抜き、残りを順番を保って前へ詰める。
        /// 動けなくなった敵はグループから抜いて交戦もやめさせ、その場に残す。残りがいなければグループを空ける。
        /// </summary>
        private void Compact(int g, NativeArray<EnemyAgent> agents, int brokenMovePartLimit)
        {
            var offset = g * EnemyFormationSettings.MAX_GROUP_SIZE;
            var group = _groups[g];
            var count = 0;

            for (var i = 0; i < group.MemberCount; i++)
            {
                var index = _members[offset + i];
                var agent = agents[index];
                if (!agent.IsAlive || agent.GroupIndex != g) continue;

                if (agent.BrokenMovePartCount >= brokenMovePartLimit)
                {
                    Leave(ref agent);
                    agents[index] = agent;
                    continue;
                }

                agent.SlotIndex = count;
                agents[index] = agent;
                _members[offset + count] = index;
                count++;
            }

            group.MemberCount = count;
            group.IsActive = count > 0;
            _groups[g] = group;
        }

        /// <summary>
        /// グループ g のメンバーを、MERGE_DISTANCE より近く、合わせても人数を超えないグループのうち、アンカーが最も近いグループの隊列の最後に移す。
        /// 移したら g を空けて true を返す。
        /// </summary>
        private bool TryMerge(int g, NativeArray<EnemyAgent> agents, in EnemyFormationSettings formation)
        {
            var source = _groups[g];
            var target = -1;
            var bestDistanceSq = MERGE_DISTANCE * MERGE_DISTANCE;

            for (var h = 0; h < _groups.Length; h++)
            {
                var other = _groups[h];
                if (h == g || !other.IsActive || other.MemberCount + source.MemberCount > formation.GroupSize) continue;

                var distanceSq = math.distancesq(other.AnchorPosition.xz, source.AnchorPosition.xz);
                if (distanceSq >= bestDistanceSq) continue;

                bestDistanceSq = distanceSq;
                target = h;
            }

            if (target < 0) return false;

            var destination = _groups[target];
            for (var i = 0; i < source.MemberCount; i++)
            {
                var index = _members[g * EnemyFormationSettings.MAX_GROUP_SIZE + i];
                var agent = agents[index];
                agent.GroupIndex = target;
                agent.SlotIndex = destination.MemberCount;
                agents[index] = agent;
                _members[target * EnemyFormationSettings.MAX_GROUP_SIZE + destination.MemberCount] = index;
                destination.MemberCount++;
            }

            _groups[target] = destination;
            source.IsActive = false;
            source.MemberCount = 0;
            _groups[g] = source;
            return true;
        }

        private void Reorder(int g, NativeArray<EnemyAgent> agents, EnemyNavigationGrid grid, NativeArray<float> distances)
        {
            var offset = g * EnemyFormationSettings.MAX_GROUP_SIZE;
            var count = _groups[g].MemberCount;
            Span<float> keys = stackalloc float[EnemyFormationSettings.MAX_GROUP_SIZE];

            for (var i = 0; i < count; i++)
            {
                var agent = agents[_members[offset + i]];
                keys[i] = float.MaxValue;
                if (!agent.IsAlive || agent.GroupIndex != g || !grid.TryGetColumn(agent.Position, out var column)) continue;

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
        /// 空いているグループを使い始め、道筋をアンカーの後ろへまっすぐ伸ばした点で埋める。空きがなければ -1。
        /// 待つ状態から始め、phaseTimer の後に進み始める。
        /// </summary>
        private int OpenGroup(float3 anchorPosition, float anchorYaw, float phaseTimer, in EnemyFormationSettings formation)
        {
            for (var g = 0; g < _groups.Length; g++)
            {
                if (_groups[g].IsActive) continue;

                var backward = -new float3(math.sin(anchorYaw), 0f, math.cos(anchorYaw));
                var pathCount = math.min(PATH_CAPACITY,
                    (int)math.ceil(formation.GroupSize * formation.RowSpacing / PATH_SPACING) + 1);
                for (var i = 0; i < pathCount; i++)
                {
                    // 最も古い点が最も後ろになるよう、区画の先頭から後ろの点を並べる
                    _paths[g * PATH_CAPACITY + i] = anchorPosition + backward * ((pathCount - 1 - i) * PATH_SPACING);
                }

                _groups[g] = new EnemyGroup
                {
                    IsActive = true,
                    AnchorPosition = anchorPosition,
                    AnchorYaw = anchorYaw,
                    AnchorDistance = float.PositiveInfinity,
                    IsAdvancing = false,
                    EncircleSlot = -1,
                    PhaseTimer = phaseTimer,
                    ColumnCount = 1,
                    PendingColumnCount = 1,
                    PathHead = pathCount - 1,
                    PathCount = pathCount
                };
                return g;
            }

            return -1;
        }
    }
}
