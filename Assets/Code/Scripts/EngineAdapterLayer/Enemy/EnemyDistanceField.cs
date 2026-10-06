using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の経路の格子（EnemyNavigationGrid）を作って持ち、プレイヤーからの距離マップを計算する。
    /// </summary>
    /// <remarks>
    /// 立てる層は、作るときに列ごとに下向きのレイを撃って床の上面を集め、上に敵の背丈の分だけ物が重ならないものを残す。
    /// 頭上の判定をレイでなく箱の重なりで行うのは、レイが始まった位置のコライダーを検出せず、壁の中の地面を立てると判定してしまう為。
    /// 隣のノードからたどり着けるかは、格子を作るときに辺（ノードごとに、そこへ進めるノードを表すビット）として求めておく。
    /// 距離はプレイヤーのいるノードから辺を逆向きにたどって Job で求める。コストは縦横 10・斜め 14 の整数にし、
    /// コストの値ごとのバケットに分けて小さい順に確定させる（Dial 法）。辺のコストの最大が 14 なので、バケットは 15 個を使い回せる。
    /// 計算は 2 つの配列を入れ替えて行い、Job が終わるまで前の結果を読めるようにする。Job は複数のフレームにまたがってよい。
    /// </remarks>
    // TODO: 区間14で、2 段の距離マップと、エディタでの事前の焼き付けを作る
    public sealed class EnemyDistanceField : IDisposable
    {
        private const int MAX_LAYERS = EnemyNavigationGrid.MAX_LAYERS;

        private const int STRAIGHT_COST = 10;
        private const int DIAGONAL_COST = 14;
        private const int BUCKET_COUNT = DIAGONAL_COST + 1;

        /// <summary> 下向きのレイ 1 本で集める床の数。重なった床と、その下の地面を拾えるよう、層の数より多くする </summary>
        private const int MAX_RAY_HITS = 8;

        /// <summary> 立てる床とみなす、法線の上向きの成分の下限（約 45 度） </summary>
        private const float MIN_FLOOR_NORMAL_Y = 0.7f;

        /// <summary> 頭上の判定の箱を、床の上面から浮かせる高さ（m）。床そのものとの重なりを避ける </summary>
        private const float CLEARANCE_OFFSET = 0.05f;

        /// <summary> 頭上の判定の箱の、水平方向の半分の幅の、マスの一辺に対する割合 </summary>
        private const float CLEARANCE_HALF_WIDTH_RATE = 0.4f;

        /// <summary> プレイヤーの足元とみなす、プレイヤーの位置より上の高さ（m） </summary>
        private const float STANDING_TOLERANCE = 0.5f;

        private const string MARKER_NAME = "Kizami.Enemy.DistanceField";

        private static readonly ProfilerMarker _marker = new(MARKER_NAME);

        private EnemyNavigationGrid _grid;

        /// <summary> 計算の済んだ、ノードごとのプレイヤーまでの距離（m）。たどり着けないノードは正の無限大 </summary>
        private NativeArray<float> _distances;

        /// <summary> Job が書き込む側の距離の配列 </summary>
        private NativeArray<float> _workingDistances;

        /// <summary> ノードごとの、そこへ進めるノード。ビット（向きの番号 × MAX_LAYERS ＋ 隣の列の層の番号）で表し、8 向き × 4 層が uint に収まる </summary>
        private NativeArray<uint> _incomingEdges;

        /// <summary> 距離の Job の作業用の、ノードごとの整数のコスト </summary>
        private NativeArray<int> _costs;

        /// <summary> 距離の Job の作業用の、同じバケットの前のノード </summary>
        private NativeArray<int> _previousInBucket;

        /// <summary> 距離の Job の作業用の、同じバケットの次のノード </summary>
        private NativeArray<int> _nextInBucket;

        private JobHandle _jobHandle;
        private bool _isRunning;

        /// <summary> 計算中の Job の始点のノード </summary>
        private int _runningStartNode = -1;

        /// <summary> _distances の始点のノード。まだ計算していなければ -1 </summary>
        private int _startNode = -1;

        private ProfilerRecorder _recorder;

        /// <summary> 経路の格子 </summary>
        public EnemyNavigationGrid Grid => _grid;

        /// <summary> 計算の済んだ、ノードごとのプレイヤーまでの距離（m）。たどり着けないノードは正の無限大。次の Update までに読み終える </summary>
        public NativeArray<float> Distances => _distances;

        /// <summary> 立てる層の総数 </summary>
        public int NodeCount { get; private set; }

        /// <summary> 1 つの列に MAX_LAYERS を超える立てる層があり、上の層を捨てた列の数 </summary>
        public int OverflowColumnCount { get; private set; }

        /// <summary> 格子を作るのにかかった時間（ms） </summary>
        public double BakeMilliseconds { get; private set; }

        /// <summary> 最後に行った距離の計算 1 回にかかった時間（ms）。計算はプレイヤーのいるノードが変わったときだけ行うので、最後の値を残す </summary>
        public double ComputeMilliseconds { get; private set; }

        /// <summary>
        /// 範囲の中の格子を、物理のクエリで作る。呼び出しの中で完了まで待つ。
        /// </summary>
        /// <param name="bounds">格子を作る範囲。レイは上面から底面まで撃つ</param>
        /// <param name="cellSize">マスの一辺（m）</param>
        /// <param name="enemyHeight">床の上に空いている必要がある高さ（m）</param>
        /// <param name="climbHeight">登れる段差の高さ（m）</param>
        /// <param name="dropHeight">歩いて降りられる段差の高さ（m）</param>
        /// <param name="groundLayers">床と障害物のレイヤー</param>
        public EnemyDistanceField(Bounds bounds, float cellSize, float enemyHeight, float climbHeight, float dropHeight,
            LayerMask groundLayers)
        {
            var width = math.max(1, (int)math.ceil(bounds.size.x / cellSize));
            var depth = math.max(1, (int)math.ceil(bounds.size.z / cellSize));
            var nodeCount = width * depth * MAX_LAYERS;

            _grid = new EnemyNavigationGrid
            {
                Heights = new NativeArray<float>(nodeCount, Allocator.Persistent),
                LayerCounts = new NativeArray<byte>(width * depth, Allocator.Persistent),
                Origin = bounds.min,
                CellSize = cellSize,
                ClimbHeight = climbHeight,
                DropHeight = dropHeight,
                Width = width,
                Depth = depth
            };
            _distances = new NativeArray<float>(nodeCount, Allocator.Persistent);
            _workingDistances = new NativeArray<float>(nodeCount, Allocator.Persistent);
            _incomingEdges = new NativeArray<uint>(nodeCount, Allocator.Persistent);
            _costs = new NativeArray<int>(nodeCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _previousInBucket = new NativeArray<int>(nodeCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _nextInBucket = new NativeArray<int>(nodeCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < nodeCount; i++) _distances[i] = float.PositiveInfinity;

            Bake(bounds, enemyHeight, groundLayers);

            _recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MARKER_NAME);
        }

        /// <summary>
        /// 毎フレーム呼ぶ。終わった計算の結果を読める側へ移し、プレイヤーのいるノードが前の計算と変わっていれば、次の計算を始める。
        /// プレイヤーの足元に立てる層がないとき（空中、壁走り、範囲の外）は、前の結果を使い続ける。
        /// </summary>
        public void Update(float3 playerPosition)
        {
            // 前のフレームに計算が走っていれば、その時間を残す
            if (_recorder.Valid && _recorder.LastValue > 0) ComputeMilliseconds = _recorder.LastValue * 1e-6;

            if (_isRunning)
            {
                if (!_jobHandle.IsCompleted) return;

                _jobHandle.Complete();
                (_distances, _workingDistances) = (_workingDistances, _distances);
                _startNode = _runningStartNode;
                _isRunning = false;
            }

            if (!_grid.TryGetColumn(playerPosition, out var column)) return;

            var node = _grid.GetHighestNodeBelow(column, playerPosition.y + STANDING_TOLERANCE);
            if (node < 0 || node == _startNode) return;

            _jobHandle = new DistanceJob
            {
                IncomingEdges = _incomingEdges,
                Width = _grid.Width,
                MetersPerCost = _grid.CellSize / STRAIGHT_COST,
                Distances = _workingDistances,
                Costs = _costs,
                PreviousInBucket = _previousInBucket,
                NextInBucket = _nextInBucket,
                StartNode = node,
                Marker = _marker
            }.Schedule();
            _runningStartNode = node;
            _isRunning = true;
        }

        /// <summary>
        /// 中心から半径の中にある立てる層を、プレイヤーまでの距離の色（近いほど赤、遠いほど青、たどり着けなければ灰）でギズモに描く。
        /// </summary>
        public void DrawGizmos(Vector3 center, float radius)
        {
            var cellSize = _grid.CellSize;
            var size = new Vector3(cellSize * CLEARANCE_HALF_WIDTH_RATE, 0.05f, cellSize * CLEARANCE_HALF_WIDTH_RATE);
            var minX = math.max(0, (int)((center.x - radius - _grid.Origin.x) / cellSize));
            var maxX = math.min(_grid.Width - 1, (int)((center.x + radius - _grid.Origin.x) / cellSize));
            var minZ = math.max(0, (int)((center.z - radius - _grid.Origin.z) / cellSize));
            var maxZ = math.min(_grid.Depth - 1, (int)((center.z + radius - _grid.Origin.z) / cellSize));

            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var column = z * _grid.Width + x;
                    for (var k = 0; k < _grid.LayerCounts[column]; k++)
                    {
                        var node = column * MAX_LAYERS + k;
                        var distance = _distances[node];
                        Gizmos.color = float.IsPositiveInfinity(distance)
                            ? Color.gray
                            : Color.HSVToRGB(math.saturate(distance / (radius * 2f)) * 0.66f, 1f, 1f);
                        Gizmos.DrawCube(_grid.GetCellCenter(column, _grid.Heights[node]), size);
                    }
                }
            }
        }

        public void Dispose()
        {
            _jobHandle.Complete();
            _recorder.Dispose();
            if (_grid.Heights.IsCreated) _grid.Heights.Dispose();
            if (_grid.LayerCounts.IsCreated) _grid.LayerCounts.Dispose();
            if (_distances.IsCreated) _distances.Dispose();
            if (_workingDistances.IsCreated) _workingDistances.Dispose();
            if (_incomingEdges.IsCreated) _incomingEdges.Dispose();
            if (_costs.IsCreated) _costs.Dispose();
            if (_previousInBucket.IsCreated) _previousInBucket.Dispose();
            if (_nextInBucket.IsCreated) _nextInBucket.Dispose();
        }

        /// <summary>
        /// 列ごとに下向きのレイで床の上面を集め、頭上が空いているものを、下から順に立てる層として残す。残した層から辺を求める。
        /// </summary>
        private void Bake(Bounds bounds, float enemyHeight, LayerMask groundLayers)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var columnCount = _grid.Width * _grid.Depth;
            var heights = _grid.Heights;
            var layerCounts = _grid.LayerCounts;
            var query = new QueryParameters(groundLayers, false, QueryTriggerInteraction.Ignore, false);

            var rays = new NativeArray<RaycastCommand>(columnCount, Allocator.TempJob);
            var rayHits = new NativeArray<RaycastHit>(columnCount * MAX_RAY_HITS, Allocator.TempJob);
            for (var column = 0; column < columnCount; column++)
            {
                var from = _grid.GetCellCenter(column, bounds.max.y);
                rays[column] = new RaycastCommand(from, Vector3.down, query, bounds.size.y);
            }

            RaycastCommand.ScheduleBatch(rays, rayHits, 64, MAX_RAY_HITS).Complete();

            // 床の候補を、列ごとに下から順に並べる。坂では箱の上り側の角が床にめり込むので、傾きの分だけ箱を浮かせる高さも持つ
            var halfWidth = _grid.CellSize * CLEARANCE_HALF_WIDTH_RATE;
            var candidateHeights = new NativeArray<float>(columnCount * MAX_RAY_HITS, Allocator.TempJob);
            var candidateLifts = new NativeArray<float>(columnCount * MAX_RAY_HITS, Allocator.TempJob);
            var candidateCounts = new NativeArray<int>(columnCount, Allocator.TempJob);
            var candidateTotal = 0;
            for (var column = 0; column < columnCount; column++)
            {
                var count = 0;
                for (var h = 0; h < MAX_RAY_HITS; h++)
                {
                    var hit = rayHits[column * MAX_RAY_HITS + h];
                    if (hit.normal.y < MIN_FLOOR_NORMAL_Y) continue;

                    var height = hit.point.y;
                    var slope = math.sqrt(1f - hit.normal.y * hit.normal.y) / hit.normal.y;
                    var lift = CLEARANCE_OFFSET + halfWidth * math.SQRT2 * slope;
                    var insert = column * MAX_RAY_HITS + count;
                    while (insert > column * MAX_RAY_HITS && candidateHeights[insert - 1] > height)
                    {
                        candidateHeights[insert] = candidateHeights[insert - 1];
                        candidateLifts[insert] = candidateLifts[insert - 1];
                        insert--;
                    }

                    candidateHeights[insert] = height;
                    candidateLifts[insert] = lift;
                    count++;
                }

                candidateCounts[column] = count;
                candidateTotal += count;
            }

            var boxes = new NativeArray<OverlapBoxCommand>(candidateTotal, Allocator.TempJob);
            var boxHits = new NativeArray<ColliderHit>(candidateTotal, Allocator.TempJob);
            var boxIndex = 0;
            for (var column = 0; column < columnCount; column++)
            {
                for (var c = 0; c < candidateCounts[column]; c++)
                {
                    var candidate = column * MAX_RAY_HITS + c;
                    var halfHeight = math.max(0.01f, (enemyHeight - candidateLifts[candidate]) * 0.5f);
                    var center = _grid.GetCellCenter(column, candidateHeights[candidate] + candidateLifts[candidate] + halfHeight);
                    boxes[boxIndex++] = new OverlapBoxCommand(center, new Vector3(halfWidth, halfHeight, halfWidth),
                        Quaternion.identity, query);
                }
            }

            OverlapBoxCommand.ScheduleBatch(boxes, boxHits, 64, 1).Complete();

            boxIndex = 0;
            for (var column = 0; column < columnCount; column++)
            {
                var layerCount = 0;
                for (var c = 0; c < candidateCounts[column]; c++)
                {
                    var isClear = boxHits[boxIndex++].collider == null;
                    if (!isClear) continue;

                    if (layerCount == MAX_LAYERS)
                    {
                        OverflowColumnCount++;
                        break;
                    }

                    heights[column * MAX_LAYERS + layerCount] = candidateHeights[column * MAX_RAY_HITS + c];
                    layerCount++;
                }

                layerCounts[column] = (byte)layerCount;
                NodeCount += layerCount;
            }

            rays.Dispose();
            rayHits.Dispose();
            candidateHeights.Dispose();
            candidateLifts.Dispose();
            candidateCounts.Dispose();
            boxes.Dispose();
            boxHits.Dispose();

            new EdgeJob
            {
                Grid = _grid,
                IncomingEdges = _incomingEdges
            }.Schedule(columnCount, 64).Complete();

            BakeMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// 列ごとに、各層のノードへ隣の列のどの層から進めるかを、移動の規則（EnemyNavigationGrid）で求めて辺のビットにする。
        /// 自分の列のノードだけを書き換える。
        /// </summary>
        [BurstCompile]
        private struct EdgeJob : IJobParallelFor
        {
            public EnemyNavigationGrid Grid;

            [NativeDisableParallelForRestriction]
            public NativeArray<uint> IncomingEdges;

            public void Execute(int column)
            {
                var x = column % Grid.Width;
                var z = column / Grid.Width;

                for (var k = 0; k < MAX_LAYERS; k++)
                {
                    var node = column * MAX_LAYERS + k;
                    IncomingEdges[node] = k < Grid.LayerCounts[column] ? GetIncomingEdges(column, x, z, node) : 0u;
                }
            }

            private uint GetIncomingEdges(int column, int x, int z, int node)
            {
                var edges = 0u;

                for (var direction = 0; direction < 8; direction++)
                {
                    var offset = EnemyNavigationGrid.GetNeighborOffset(direction);
                    var nx = x + offset.x;
                    var nz = z + offset.y;
                    if (nx < 0 || nx >= Grid.Width || nz < 0 || nz >= Grid.Depth) continue;

                    var isDiagonal = offset.x != 0 && offset.y != 0;
                    var fromColumn = nz * Grid.Width + nx;

                    for (var k = 0; k < Grid.LayerCounts[fromColumn]; k++)
                    {
                        var fromHeight = Grid.Heights[fromColumn * MAX_LAYERS + k];
                        if (Grid.GetLandingNode(column, fromHeight) != node) continue;

                        if (isDiagonal && !(Grid.CanCross(z * Grid.Width + nx, fromHeight)
                                            && Grid.CanCross(nz * Grid.Width + x, fromHeight))) continue;

                        edges |= 1u << (direction * MAX_LAYERS + k);
                    }
                }

                return edges;
            }
        }

        /// <summary>
        /// 始点のノードから、各ノードがそこへたどり着くまでの距離を求める。
        /// 未確定のノードは、コストを BUCKET_COUNT で割った余りのバケット（双方向の連結リスト）に 1 つだけ入れ、コストが下がったら入れ替える。
        /// </summary>
        [BurstCompile]
        private struct DistanceJob : IJob
        {
            [ReadOnly] public NativeArray<uint> IncomingEdges;
            public int Width;

            /// <summary> 整数のコスト 1 あたりの距離（m） </summary>
            public float MetersPerCost;

            public NativeArray<float> Distances;
            public NativeArray<int> Costs;
            public NativeArray<int> PreviousInBucket;
            public NativeArray<int> NextInBucket;
            public int StartNode;
            public ProfilerMarker Marker;

            public void Execute()
            {
                using var scope = Marker.Auto();

                for (var i = 0; i < Distances.Length; i++)
                {
                    Distances[i] = float.PositiveInfinity;
                    Costs[i] = int.MaxValue;
                }

                var heads = new NativeArray<int>(BUCKET_COUNT, Allocator.Temp);
                for (var b = 0; b < BUCKET_COUNT; b++) heads[b] = -1;

                Costs[StartNode] = 0;
                Insert(ref heads, StartNode, 0);
                var queuedCount = 1;

                for (var cost = 0; queuedCount > 0; cost++)
                {
                    var bucket = cost % BUCKET_COUNT;
                    while (heads[bucket] >= 0)
                    {
                        var node = heads[bucket];
                        Remove(ref heads, node, bucket);
                        queuedCount--;

                        Distances[node] = cost * MetersPerCost;
                        Relax(ref heads, node, cost, ref queuedCount);
                    }
                }

                heads.Dispose();
            }

            /// <summary>
            /// 確定したノードへ進める隣のノードのコストを、cost ＋ 辺のコストまで下げる。
            /// 辺のコストは 10 以上なので、確定したノードのコストが下がることはない。
            /// </summary>
            private void Relax(ref NativeArray<int> heads, int node, int cost, ref int queuedCount)
            {
                var column = node / MAX_LAYERS;
                var x = column % Width;
                var z = column / Width;
                var edges = IncomingEdges[node];

                while (edges != 0u)
                {
                    var bit = math.tzcnt(edges);
                    edges &= edges - 1u;

                    var offset = EnemyNavigationGrid.GetNeighborOffset(bit / MAX_LAYERS);
                    var from = ((z + offset.y) * Width + x + offset.x) * MAX_LAYERS + bit % MAX_LAYERS;
                    var newCost = cost + (offset.x != 0 && offset.y != 0 ? DIAGONAL_COST : STRAIGHT_COST);
                    if (newCost >= Costs[from]) continue;

                    if (Costs[from] == int.MaxValue)
                    {
                        queuedCount++;
                    }
                    else
                    {
                        Remove(ref heads, from, Costs[from] % BUCKET_COUNT);
                    }

                    Costs[from] = newCost;
                    Insert(ref heads, from, newCost % BUCKET_COUNT);
                }
            }

            private void Insert(ref NativeArray<int> heads, int node, int bucket)
            {
                var head = heads[bucket];
                PreviousInBucket[node] = -1;
                NextInBucket[node] = head;
                if (head >= 0) PreviousInBucket[head] = node;

                heads[bucket] = node;
            }

            private void Remove(ref NativeArray<int> heads, int node, int bucket)
            {
                var previous = PreviousInBucket[node];
                var next = NextInBucket[node];
                if (previous >= 0)
                {
                    NextInBucket[previous] = next;
                }
                else
                {
                    heads[bucket] = next;
                }

                if (next >= 0) PreviousInBucket[next] = previous;
            }
        }
    }
}
