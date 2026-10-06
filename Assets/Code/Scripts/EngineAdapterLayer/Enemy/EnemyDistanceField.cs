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
    /// 敵の経路に使う格子と、プレイヤーからの距離マップ。
    /// 格子は縦の列ごとに「立てる層」（敵が立てる床の高さ）を下から順に持ち、層 1 つを 1 つのノードとして距離を持つ。
    /// </summary>
    /// <remarks>
    /// 立てる層は、作るときに列ごとに下向きのレイを撃って床の上面を集め、上に敵の背丈の分だけ物が重ならないものを残す。
    /// 頭上の判定をレイでなく箱の重なりで行うのは、レイが始まった位置のコライダーを検出せず、壁の中の地面を立てると判定してしまう為。
    /// 距離はプレイヤーのいるノードから逆向きに、ダイクストラ法（斜めは √2 倍）で Job で求める。
    /// 敵がノード n から隣の列へ進むと、その列のうち「n の高さ＋登れる高さ」以下で最も高い層に乗る。登るのは登れる高さまでで、降りるのは高さに制限がない。
    /// 計算は 2 つの配列を入れ替えて行い、Job が終わるまで前の結果を読めるようにする。Job は複数のフレームにまたがってよい。
    /// </remarks>
    // TODO: 区間4C で、2 段の距離マップ、エディタでの事前の焼き付け、ボクセルが壊れた範囲の調べ直しを作る
    public sealed class EnemyDistanceField : IDisposable
    {
        /// <summary> 1 つの列に持てる立てる層の数 </summary>
        public const int MAX_LAYERS = 4;

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

        private const float DIAGONAL_COST = 1.41421356f;

        private const string MARKER_NAME = "Kizami.Enemy.DistanceField";

        private static readonly ProfilerMarker _marker = new(MARKER_NAME);

        private readonly float3 _origin;
        private readonly float _cellSize;
        private readonly float _climbHeight;
        private readonly int _width;
        private readonly int _depth;

        /// <summary> ノードごとの床の高さ。ノード番号は 列番号 × MAX_LAYERS ＋ 層の番号 </summary>
        private NativeArray<float> _heights;

        /// <summary> 列ごとの立てる層の数。列番号は z × 幅 ＋ x </summary>
        private NativeArray<byte> _layerCounts;

        /// <summary> 計算の済んだ、ノードごとのプレイヤーまでの距離（m）。たどり着けないノードは正の無限大 </summary>
        private NativeArray<float> _distances;

        /// <summary> Job が書き込む側の距離の配列 </summary>
        private NativeArray<float> _workingDistances;

        private JobHandle _jobHandle;
        private bool _isRunning;

        /// <summary> 計算中の Job の始点のノード </summary>
        private int _runningStartNode = -1;

        /// <summary> _distances の始点のノード。まだ計算していなければ -1 </summary>
        private int _startNode = -1;

        private ProfilerRecorder _recorder;

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
        /// <param name="groundLayers">床と障害物のレイヤー</param>
        public EnemyDistanceField(Bounds bounds, float cellSize, float enemyHeight, float climbHeight, LayerMask groundLayers)
        {
            _origin = bounds.min;
            _cellSize = cellSize;
            _climbHeight = climbHeight;
            _width = math.max(1, (int)math.ceil(bounds.size.x / cellSize));
            _depth = math.max(1, (int)math.ceil(bounds.size.z / cellSize));

            var nodeCount = _width * _depth * MAX_LAYERS;
            _heights = new NativeArray<float>(nodeCount, Allocator.Persistent);
            _layerCounts = new NativeArray<byte>(_width * _depth, Allocator.Persistent);
            _distances = new NativeArray<float>(nodeCount, Allocator.Persistent);
            _workingDistances = new NativeArray<float>(nodeCount, Allocator.Persistent);
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

            if (!TryGetStandingNode(playerPosition, out var node) || node == _startNode) return;

            _jobHandle = new DistanceJob
            {
                Heights = _heights,
                LayerCounts = _layerCounts,
                Distances = _workingDistances,
                Width = _width,
                Depth = _depth,
                CellSize = _cellSize,
                ClimbHeight = _climbHeight,
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
            var size = new Vector3(_cellSize * CLEARANCE_HALF_WIDTH_RATE, 0.05f, _cellSize * CLEARANCE_HALF_WIDTH_RATE);
            var minX = math.max(0, (int)((center.x - radius - _origin.x) / _cellSize));
            var maxX = math.min(_width - 1, (int)((center.x + radius - _origin.x) / _cellSize));
            var minZ = math.max(0, (int)((center.z - radius - _origin.z) / _cellSize));
            var maxZ = math.min(_depth - 1, (int)((center.z + radius - _origin.z) / _cellSize));

            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var column = z * _width + x;
                    for (var k = 0; k < _layerCounts[column]; k++)
                    {
                        var node = column * MAX_LAYERS + k;
                        var distance = _distances[node];
                        Gizmos.color = float.IsPositiveInfinity(distance)
                            ? Color.gray
                            : Color.HSVToRGB(math.saturate(distance / (radius * 2f)) * 0.66f, 1f, 1f);
                        Gizmos.DrawCube(GetCellCenter(x, z, _heights[node]), size);
                    }
                }
            }
        }

        public void Dispose()
        {
            _jobHandle.Complete();
            _recorder.Dispose();
            if (_heights.IsCreated) _heights.Dispose();
            if (_layerCounts.IsCreated) _layerCounts.Dispose();
            if (_distances.IsCreated) _distances.Dispose();
            if (_workingDistances.IsCreated) _workingDistances.Dispose();
        }

        private Vector3 GetCellCenter(int x, int z, float height)
        {
            return new Vector3(_origin.x + (x + 0.5f) * _cellSize, height, _origin.z + (z + 0.5f) * _cellSize);
        }

        /// <summary>
        /// 位置の真下の列のうち、位置より少し上までにある最も高い層を返す。
        /// </summary>
        private bool TryGetStandingNode(float3 position, out int node)
        {
            node = -1;

            var x = (int)math.floor((position.x - _origin.x) / _cellSize);
            var z = (int)math.floor((position.z - _origin.z) / _cellSize);
            if (x < 0 || x >= _width || z < 0 || z >= _depth) return false;

            var column = z * _width + x;
            for (var k = 0; k < _layerCounts[column]; k++)
            {
                if (_heights[column * MAX_LAYERS + k] > position.y + STANDING_TOLERANCE) break;

                node = column * MAX_LAYERS + k;
            }

            return node >= 0;
        }

        /// <summary>
        /// 列ごとに下向きのレイで床の上面を集め、頭上が空いているものを、下から順に立てる層として残す。
        /// </summary>
        private void Bake(Bounds bounds, float enemyHeight, LayerMask groundLayers)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var columnCount = _width * _depth;
            var query = new QueryParameters(groundLayers, false, QueryTriggerInteraction.Ignore, false);

            var rays = new NativeArray<RaycastCommand>(columnCount, Allocator.TempJob);
            var rayHits = new NativeArray<RaycastHit>(columnCount * MAX_RAY_HITS, Allocator.TempJob);
            for (var z = 0; z < _depth; z++)
            {
                for (var x = 0; x < _width; x++)
                {
                    var from = GetCellCenter(x, z, bounds.max.y);
                    rays[z * _width + x] = new RaycastCommand(from, Vector3.down, query, bounds.size.y);
                }
            }

            RaycastCommand.ScheduleBatch(rays, rayHits, 64, MAX_RAY_HITS).Complete();

            // 床の候補を、列ごとに下から順に並べる。坂では箱の上り側の角が床にめり込むので、傾きの分だけ箱を浮かせる高さも持つ
            var halfWidth = _cellSize * CLEARANCE_HALF_WIDTH_RATE;
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
                    var center = GetCellCenter(column % _width, column / _width,
                        candidateHeights[candidate] + candidateLifts[candidate] + halfHeight);
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

                    _heights[column * MAX_LAYERS + layerCount] = candidateHeights[column * MAX_RAY_HITS + c];
                    layerCount++;
                }

                _layerCounts[column] = (byte)layerCount;
                NodeCount += layerCount;
            }

            rays.Dispose();
            rayHits.Dispose();
            candidateHeights.Dispose();
            candidateLifts.Dispose();
            candidateCounts.Dispose();
            boxes.Dispose();
            boxHits.Dispose();

            BakeMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// 始点のノードから、各ノードがそこへたどり着くまでの距離を求める。
        /// </summary>
        [BurstCompile]
        private struct DistanceJob : IJob
        {
            [ReadOnly] public NativeArray<float> Heights;
            [ReadOnly] public NativeArray<byte> LayerCounts;
            public NativeArray<float> Distances;
            public int Width;
            public int Depth;
            public float CellSize;
            public float ClimbHeight;
            public int StartNode;
            public ProfilerMarker Marker;

            public void Execute()
            {
                using var scope = Marker.Auto();

                for (var i = 0; i < Distances.Length; i++) Distances[i] = float.PositiveInfinity;

                var heap = new NativeList<HeapItem>(1024, Allocator.Temp);
                Distances[StartNode] = 0f;
                Push(ref heap, new HeapItem { Distance = 0f, Node = StartNode });

                while (heap.Length > 0)
                {
                    var item = Pop(ref heap);
                    if (item.Distance > Distances[item.Node]) continue;

                    var column = item.Node / MAX_LAYERS;
                    var x = column % Width;
                    var z = column / Width;

                    for (var dz = -1; dz <= 1; dz++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;

                            var nx = x + dx;
                            var nz = z + dz;
                            if (nx < 0 || nx >= Width || nz < 0 || nz >= Depth) continue;

                            var isDiagonal = dx != 0 && dz != 0;
                            var cost = isDiagonal ? DIAGONAL_COST * CellSize : CellSize;
                            var fromColumn = nz * Width + nx;

                            // 隣の列 fromColumn の各層から、この列へ進んだときに乗る層がこのノードなら、そこからたどり着ける
                            for (var k = 0; k < LayerCounts[fromColumn]; k++)
                            {
                                var from = fromColumn * MAX_LAYERS + k;
                                var fromHeight = Heights[from];
                                if (GetLandingNode(column, fromHeight) != item.Node) continue;

                                // 斜めは、間の 2 つの列をまたげるときだけ進める（壁の角を抜けない為）
                                if (isDiagonal && !(CanCross(z * Width + nx, fromHeight) && CanCross(nz * Width + x, fromHeight))) continue;

                                var distance = item.Distance + cost;
                                if (distance >= Distances[from]) continue;

                                Distances[from] = distance;
                                Push(ref heap, new HeapItem { Distance = distance, Node = from });
                            }
                        }
                    }
                }

                heap.Dispose();
            }

            /// <summary>
            /// 高さ fromHeight から列 column へ進んだときに乗る層（fromHeight ＋ 登れる高さ以下で最も高い層）。なければ -1。
            /// </summary>
            private int GetLandingNode(int column, float fromHeight)
            {
                var node = -1;
                for (var k = 0; k < LayerCounts[column]; k++)
                {
                    if (Heights[column * MAX_LAYERS + k] > fromHeight + ClimbHeight) break;

                    node = column * MAX_LAYERS + k;
                }

                return node;
            }

            /// <summary>
            /// 高さ fromHeight から列 column へ進んだときに乗る層が、登れる高さの範囲で上下するだけか。
            /// </summary>
            private bool CanCross(int column, float fromHeight)
            {
                var node = GetLandingNode(column, fromHeight);
                return node >= 0 && math.abs(Heights[node] - fromHeight) <= ClimbHeight;
            }

            private static void Push(ref NativeList<HeapItem> heap, HeapItem item)
            {
                heap.Add(item);
                var index = heap.Length - 1;
                while (index > 0)
                {
                    var parent = (index - 1) / 2;
                    if (heap[parent].Distance <= item.Distance) break;

                    heap[index] = heap[parent];
                    index = parent;
                }

                heap[index] = item;
            }

            private static HeapItem Pop(ref NativeList<HeapItem> heap)
            {
                var top = heap[0];
                var last = heap[heap.Length - 1];
                heap.RemoveAt(heap.Length - 1);
                if (heap.Length == 0) return top;

                var index = 0;
                while (true)
                {
                    var child = index * 2 + 1;
                    if (child >= heap.Length) break;

                    if (child + 1 < heap.Length && heap[child + 1].Distance < heap[child].Distance) child++;
                    if (heap[child].Distance >= last.Distance) break;

                    heap[index] = heap[child];
                    index = child;
                }

                heap[index] = last;
                return top;
            }
        }

        private struct HeapItem
        {
            public float Distance;
            public int Node;
        }
    }
}
