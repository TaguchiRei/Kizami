using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループ（EnemyGroup）と、その道筋とメンバーの配列を持つ。EnemySpawnAdapter が、生成した敵を順にグループへ入れ、毎フレーム EnemyGroupJob を回す。
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

        private NativeArray<EnemyGroup> _groups;

        /// <summary> グループごとに PATH_CAPACITY 個の区画を持つ道筋の点 </summary>
        private NativeArray<float3> _paths;

        /// <summary> グループごとに EnemyFormationSettings.MAX_GROUP_SIZE 個の区画を持つ、メンバーの敵の番号 </summary>
        private NativeArray<int> _members;

        /// <summary> 生成した敵を入れていくグループ。次に入れる敵で新しいグループを作るなら -1 </summary>
        private int _openGroup = -1;

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

        /// <param name="capacity">グループの数の上限</param>
        public EnemyGroups(int capacity)
        {
            _groups = new NativeArray<EnemyGroup>(capacity, Allocator.Persistent);
            _paths = new NativeArray<float3>(capacity * PATH_CAPACITY, Allocator.Persistent);
            _members = new NativeArray<int>(capacity * EnemyFormationSettings.MAX_GROUP_SIZE, Allocator.Persistent);
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
            in EnemyFormationSettings formation, float moveSpeed, float deltaTime)
        {
            return new EnemyGroupJob
            {
                Groups = _groups,
                Paths = _paths,
                Members = _members,
                Agents = agents,
                Grid = grid,
                Distances = distances,
                Formation = formation,
                MoveSpeed = moveSpeed,
                DeltaTime = deltaTime
            }.Schedule();
        }

        public void Dispose()
        {
            if (_groups.IsCreated) _groups.Dispose();
            if (_paths.IsCreated) _paths.Dispose();
            if (_members.IsCreated) _members.Dispose();
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
