using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
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
    /// 頭上の判定をレイでなく箱の重なりで行うのは、レイが始まった位置のコライダーを検出せず、壁の中の地面を立てると判定してしまう為。
    /// 計算は 2 つの配列を入れ替えて行い、Job が終わるまで前の結果を読めるようにする。Job は複数のフレームにまたがってよい。
    /// </remarks>
    // TODO: 2 段の距離マップと、エディタでの事前の焼き付けを作る
    public sealed class EnemyDistanceField : IDisposable
    {
        private const int MAX_LAYERS = EnemyNavigationGrid.MAX_LAYERS;

        private const int STRAIGHT_COST = 10;
        private const int DIAGONAL_COST = 14;
        private const int BUCKET_COUNT = DIAGONAL_COST + 1;

        /// <summary> 下向きのレイ 1 本で集める床の数。重なった床と、その下の地面を拾えるよう、層の数より多くする </summary>
        private const int MAX_RAY_HITS = 8;

        /// <summary> 頭上の判定の箱 1 つで集めるコライダーの数。動いている Rigidbody のコライダーを飛ばして、その奥の物を見る為に 1 より多くする </summary>
        private const int MAX_BOX_HITS = 4;

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

        /// <summary> 調べ直しを待っている、形が変わった範囲 </summary>
        private readonly List<PendingRegion> _pendingRegions = new();

        /// <summary> ボクセルのピースから切り離された、Rigidbody を持つ塊 </summary>
        private readonly List<SeparatedPiece> _separatedPieces = new();

        /// <summary> ボクセルのモデルの通知の登録。Dispose で解除する </summary>
        private readonly List<IDisposable> _subscriptions = new();

        /// <summary> 列ごとの、MAX_LAYERS を超える立てる層があったか </summary>
        private readonly bool[] _isOverflowColumn;

        /// <summary> 床の上に空いている必要がある高さ（m） </summary>
        private readonly float _enemyHeight;

        /// <summary> 床と障害物のレイヤー </summary>
        private readonly LayerMask _groundLayers;

        /// <summary> 下向きのレイを撃ち始める高さ（格子の範囲の上面） </summary>
        private readonly float _rayTop;

        /// <summary> 下向きのレイの長さ（格子の範囲の高さ） </summary>
        private readonly float _rayLength;

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

        /// <summary> 最後に形が変わった範囲の列を調べ直したフレームで、調べ直しにかかった時間（ms） </summary>
        public double RebakeMilliseconds { get; private set; }

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

            _isOverflowColumn = new bool[width * depth];
            _enemyHeight = enemyHeight;
            _groundLayers = groundLayers;
            _rayTop = bounds.max.y;
            _rayLength = bounds.size.y;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            BakeColumns(int2.zero, new int2(width - 1, depth - 1));
            BakeMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

            _recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MARKER_NAME);
        }

        /// <summary>
        /// ローカル空間の範囲を Transform でワールド空間へ移し、8 つの角を囲む範囲を返す。
        /// </summary>
        private static Bounds TransformBounds(Transform transform, Bounds localBounds)
        {
            var worldBounds = new Bounds(transform.TransformPoint(localBounds.min), Vector3.zero);
            for (var corner = 1; corner < 8; corner++)
            {
                var local = new Vector3(
                    (corner & 1) == 0 ? localBounds.min.x : localBounds.max.x,
                    (corner & 2) == 0 ? localBounds.min.y : localBounds.max.y,
                    (corner & 4) == 0 ? localBounds.min.z : localBounds.max.z);
                worldBounds.Encapsulate(transform.TransformPoint(local));
            }

            return worldBounds;
        }

        private static bool IsMoving(Rigidbody body)
        {
            return body != null && !body.isKinematic && !body.IsSleeping();
        }

        /// <summary>
        /// 頭上の判定の箱に重なったコライダーが、動いている Rigidbody のものだけなら true。
        /// </summary>
        private static bool IsClear(NativeArray<ColliderHit> hits, int start)
        {
            for (var h = 0; h < MAX_BOX_HITS; h++)
            {
                var collider = hits[start + h].collider;
                if (collider == null) break;
                if (!IsMoving(collider.attachedRigidbody)) return false;
            }

            return true;
        }

        /// <summary>
        /// 毎フレーム呼ぶ。終わった計算の結果を読める側へ移し、形が変わった範囲の列を調べ直す。
        /// 調べ直したか、プレイヤーのいるノードが前の計算と変わっていれば、次の計算を始める。
        /// 格子を書き換えるのは距離の計算が走っていない間だけで、調べ直してから次の計算が終わるまでは、前の距離を読む。
        /// プレイヤーの足元に立てる層がないとき（空中、壁走り、範囲の外）は、前の結果を使い続ける。
        /// </summary>
        public void Update(float3 playerPosition)
        {
            // 前のフレームに計算が走っていれば、その時間を残す
            if (_recorder.Valid && _recorder.LastValue > 0) ComputeMilliseconds = _recorder.LastValue * 1e-6;

            UpdateSeparatedPieces();

            if (_isRunning)
            {
                if (!_jobHandle.IsCompleted) return;

                _jobHandle.Complete();
                (_distances, _workingDistances) = (_workingDistances, _distances);
                _startNode = _runningStartNode;
                _isRunning = false;
            }

            if (RebakeReadyRegions()) _startNode = -1;

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
        /// ボクセルのモデルを見張り、読み込み・削る編集・塊の分離で形が変わった範囲の列を、そのピースのメッシュと当たり判定の作り直しが済んでから調べ直す。
        /// 通知を受けた時点では当たり判定が古い形のままなので、作り直しを待つ。
        /// </summary>
        public void Watch(VoxelModelLoader loader)
        {
            _subscriptions.Add(loader.RegisterOnLoaded(OnModelLoaded));
            _subscriptions.Add(loader.RegisterOnPieceShapeChanged(OnPieceShapeChanged));
            _subscriptions.Add(loader.RegisterOnPieceSplit(OnPieceSplit));
            _subscriptions.Add(loader.RegisterOnPieceDestroyed(OnPieceDestroyed));

            if (loader.IsLoaded) OnModelLoaded(loader);
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

        /// <summary>
        /// 範囲の列を調べ直す。列ごとに下向きのレイで床の上面を集め、頭上が空いているものを、下から順に立てる層として残す。
        /// 動いている Rigidbody に付いたコライダー（落ちているボクセルの塊など）は、床にも障害物にも数えない。
        /// 調べ直した列と、その周りの 1 列のノードの辺を求め直す。
        /// </summary>
        /// <param name="min">調べ直す列の範囲の、最小の (x, z)</param>
        /// <param name="max">調べ直す列の範囲の、最大の (x, z)。この列も含む</param>
        private void BakeColumns(int2 min, int2 max)
        {
            var rectWidth = max.x - min.x + 1;
            var columnCount = rectWidth * (max.y - min.y + 1);
            var heights = _grid.Heights;
            var layerCounts = _grid.LayerCounts;
            var query = new QueryParameters(_groundLayers, false, QueryTriggerInteraction.Ignore, false);

            var rays = new NativeArray<RaycastCommand>(columnCount, Allocator.TempJob);
            var rayHits = new NativeArray<RaycastHit>(columnCount * MAX_RAY_HITS, Allocator.TempJob);
            for (var i = 0; i < columnCount; i++)
            {
                var from = _grid.GetCellCenter(GetColumn(min, rectWidth, i), _rayTop);
                rays[i] = new RaycastCommand(from, Vector3.down, query, _rayLength);
            }

            RaycastCommand.ScheduleBatch(rays, rayHits, 64, MAX_RAY_HITS).Complete();

            // 床の候補を、列ごとに下から順に並べる。坂では箱の上り側の角が床にめり込むので、傾きの分だけ箱を浮かせる高さも持つ
            var halfWidth = _grid.CellSize * CLEARANCE_HALF_WIDTH_RATE;
            var candidateHeights = new NativeArray<float>(columnCount * MAX_RAY_HITS, Allocator.TempJob);
            var candidateLifts = new NativeArray<float>(columnCount * MAX_RAY_HITS, Allocator.TempJob);
            var candidateCounts = new NativeArray<int>(columnCount, Allocator.TempJob);
            var candidateTotal = 0;
            for (var i = 0; i < columnCount; i++)
            {
                var count = 0;
                for (var h = 0; h < MAX_RAY_HITS; h++)
                {
                    var hit = rayHits[i * MAX_RAY_HITS + h];
                    if (hit.normal.y < MIN_FLOOR_NORMAL_Y || IsMoving(hit.rigidbody)) continue;

                    var height = hit.point.y;
                    var slope = math.sqrt(1f - hit.normal.y * hit.normal.y) / hit.normal.y;
                    var lift = CLEARANCE_OFFSET + halfWidth * math.SQRT2 * slope;
                    var insert = i * MAX_RAY_HITS + count;
                    while (insert > i * MAX_RAY_HITS && candidateHeights[insert - 1] > height)
                    {
                        candidateHeights[insert] = candidateHeights[insert - 1];
                        candidateLifts[insert] = candidateLifts[insert - 1];
                        insert--;
                    }

                    candidateHeights[insert] = height;
                    candidateLifts[insert] = lift;
                    count++;
                }

                candidateCounts[i] = count;
                candidateTotal += count;
            }

            var boxes = new NativeArray<OverlapBoxCommand>(candidateTotal, Allocator.TempJob);
            var boxHits = new NativeArray<ColliderHit>(candidateTotal * MAX_BOX_HITS, Allocator.TempJob);
            var boxIndex = 0;
            for (var i = 0; i < columnCount; i++)
            {
                var column = GetColumn(min, rectWidth, i);
                for (var c = 0; c < candidateCounts[i]; c++)
                {
                    var candidate = i * MAX_RAY_HITS + c;
                    var halfHeight = math.max(0.01f, (_enemyHeight - candidateLifts[candidate]) * 0.5f);
                    var center = _grid.GetCellCenter(column, candidateHeights[candidate] + candidateLifts[candidate] + halfHeight);
                    boxes[boxIndex++] = new OverlapBoxCommand(center, new Vector3(halfWidth, halfHeight, halfWidth),
                        Quaternion.identity, query);
                }
            }

            OverlapBoxCommand.ScheduleBatch(boxes, boxHits, 64, MAX_BOX_HITS).Complete();

            boxIndex = 0;
            for (var i = 0; i < columnCount; i++)
            {
                var column = GetColumn(min, rectWidth, i);
                NodeCount -= layerCounts[column];
                if (_isOverflowColumn[column]) OverflowColumnCount--;

                var layerCount = 0;
                var isOverflow = false;
                for (var c = 0; c < candidateCounts[i]; c++)
                {
                    var isClear = IsClear(boxHits, boxIndex++ * MAX_BOX_HITS);
                    if (!isClear || isOverflow) continue;

                    if (layerCount == MAX_LAYERS)
                    {
                        isOverflow = true;
                        continue;
                    }

                    heights[column * MAX_LAYERS + layerCount] = candidateHeights[i * MAX_RAY_HITS + c];
                    layerCount++;
                }

                layerCounts[column] = (byte)layerCount;
                NodeCount += layerCount;
                _isOverflowColumn[column] = isOverflow;
                if (isOverflow) OverflowColumnCount++;
            }

            rays.Dispose();
            rayHits.Dispose();
            candidateHeights.Dispose();
            candidateLifts.Dispose();
            candidateCounts.Dispose();
            boxes.Dispose();
            boxHits.Dispose();

            // 隣の列の層が変わると、その列へ進む辺も変わるので、周りの 1 列まで求め直す
            var edgeMin = math.max(min - 1, 0);
            var edgeMax = math.min(max + 1, new int2(_grid.Width - 1, _grid.Depth - 1));
            var edgeRectWidth = edgeMax.x - edgeMin.x + 1;
            new EdgeJob
            {
                Grid = _grid,
                IncomingEdges = _incomingEdges,
                Min = edgeMin,
                RectWidth = edgeRectWidth
            }.Schedule(edgeRectWidth * (edgeMax.y - edgeMin.y + 1), 64).Complete();
        }

        /// <summary>
        /// 範囲の中で i 番目の列の列番号を返す。範囲の中は x、z の順に並べる。
        /// </summary>
        private int GetColumn(int2 min, int rectWidth, int i)
        {
            return (min.y + i / rectWidth) * _grid.Width + min.x + i % rectWidth;
        }

        /// <summary>
        /// ワールド空間の範囲に重なる列の範囲を返す。格子の外なら false。
        /// </summary>
        private bool TryGetColumnRect(Bounds bounds, out int2 min, out int2 max)
        {
            var origin = _grid.Origin.xz;
            min = math.max((int2)math.floor((((float3)bounds.min).xz - origin) / _grid.CellSize), 0);
            max = math.min((int2)math.floor((((float3)bounds.max).xz - origin) / _grid.CellSize),
                new int2(_grid.Width - 1, _grid.Depth - 1));
            return math.all(min <= max);
        }

        /// <summary>
        /// 調べ直しを待っている範囲のうち、ピースの作り直しが済んだものの列を調べ直す。調べ直したら true。
        /// </summary>
        private bool RebakeReadyRegions()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var hasRebaked = false;

            for (var i = _pendingRegions.Count - 1; i >= 0; i--)
            {
                var region = _pendingRegions[i];
                var hasPiece = region.Piece != null;
                if (hasPiece && region.Piece.PendingChunkCount > 0) continue;

                _pendingRegions.RemoveAt(i);
                if (region.UsesPieceBounds && !hasPiece) continue;

                var bounds = region.UsesPieceBounds ? region.Piece.WorldBounds : region.Bounds;
                if (!TryGetColumnRect(bounds, out var min, out var max)) continue;

                BakeColumns(min, max);
                hasRebaked = true;
            }

            if (hasRebaked) RebakeMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            return hasRebaked;
        }

        /// <summary>
        /// 切り離された塊が止まったら（Rigidbody が眠ったら）その範囲を足場か障害物にし、また動き出したらその範囲を空ける。
        /// </summary>
        private void UpdateSeparatedPieces()
        {
            for (var i = _separatedPieces.Count - 1; i >= 0; i--)
            {
                var separated = _separatedPieces[i];
                if (separated.Piece == null || separated.Body == null)
                {
                    _separatedPieces.RemoveAt(i);
                    continue;
                }

                var isResting = separated.Body.IsSleeping();
                if (isResting == separated.IsResting) continue;

                separated.IsResting = isResting;
                _separatedPieces[i] = separated;
                _pendingRegions.Add(new PendingRegion(separated.Piece, separated.Piece.WorldBounds, false));
            }
        }

        private void OnModelLoaded(VoxelModelLoader loader)
        {
            foreach (var part in loader.Parts)
            {
                _pendingRegions.Add(new PendingRegion(part, default, true));
            }
        }

        private void OnPieceShapeChanged(VoxelShapeChange change)
        {
            var bounds = TransformBounds(change.Piece.transform, change.LocalBounds);
            _pendingRegions.Add(new PendingRegion(change.Piece, bounds, false));
        }

        /// <summary>
        /// 切り離された塊があった範囲は、元のピースの作り直しが済んでから空ける。塊は、止まるまで格子に入れない。
        /// </summary>
        private void OnPieceSplit(VoxelPiece[] pieces)
        {
            for (var i = 1; i < pieces.Length; i++)
            {
                var piece = pieces[i];
                _pendingRegions.Add(new PendingRegion(pieces[0], piece.WorldBounds, false));
                if (piece.TryGetComponent<Rigidbody>(out var body))
                {
                    _separatedPieces.Add(new SeparatedPiece { Piece = piece, Body = body, IsResting = false });
                }
            }
        }

        private void OnPieceDestroyed(VoxelPiece piece)
        {
            _pendingRegions.Add(new PendingRegion(null, piece.WorldBounds, false));
        }

        public void Dispose()
        {
            _jobHandle.Complete();
            _recorder.Dispose();
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
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
        /// 範囲の列ごとに、各層のノードへ隣の列のどの層から進めるかを、移動の規則（EnemyNavigationGrid）で求めて辺のビットにする。
        /// 自分の列のノードだけを書き換える。
        /// </summary>
        [BurstCompile]
        private struct EdgeJob : IJobParallelFor
        {
            public EnemyNavigationGrid Grid;

            [NativeDisableParallelForRestriction]
            public NativeArray<uint> IncomingEdges;

            /// <summary> 範囲の最小の (x, z) </summary>
            public int2 Min;

            /// <summary> 範囲の x 方向の列の数 </summary>
            public int RectWidth;

            public void Execute(int index)
            {
                var x = Min.x + index % RectWidth;
                var z = Min.y + index / RectWidth;
                var column = z * Grid.Width + x;

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
        /// 始点のノードから辺を逆向きにたどり、各ノードがそこへたどり着くまでの距離を求める。
        /// コストは縦横 STRAIGHT_COST・斜め DIAGONAL_COST の整数にし、コストの値ごとのバケットに分けて小さい順に確定させる（Dial 法）。
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

        /// <summary>
        /// 調べ直しを待っている、形が変わった範囲。
        /// </summary>
        private readonly struct PendingRegion
        {
            /// <summary> 作り直しが済むのを待つピース。待たなくてよいなら null </summary>
            public readonly VoxelPiece Piece;

            /// <summary> 調べ直すワールド空間の範囲 </summary>
            public readonly Bounds Bounds;

            /// <summary> Bounds の代わりに、作り直しが済んだときのピースの範囲を調べ直すか。読み込んだ直後は面がなく、範囲が分からない為 </summary>
            public readonly bool UsesPieceBounds;

            public PendingRegion(VoxelPiece piece, Bounds bounds, bool usesPieceBounds)
            {
                Piece = piece;
                Bounds = bounds;
                UsesPieceBounds = usesPieceBounds;
            }
        }

        /// <summary>
        /// ボクセルのピースから切り離された、Rigidbody を持つ塊。
        /// </summary>
        private struct SeparatedPiece
        {
            public VoxelPiece Piece;
            public Rigidbody Body;

            /// <summary> 前に調べたときに止まっていたか </summary>
            public bool IsResting;
        }
    }
}
