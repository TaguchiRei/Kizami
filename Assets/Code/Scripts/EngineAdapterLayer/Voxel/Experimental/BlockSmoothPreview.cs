using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Kizami.EngineAdapter.Voxel.Experimental
{
    /// <summary>
    /// 子の VoxelPiece を、角ばった頂点と滑らかな頂点を丸みで混ぜるメッシュで描き直し、今の描画と見比べる試作のコンポーネント。
    /// 削る処理と当たり判定は今のピースのまま使い、ここではボリュームを読むだけにする。
    /// ピースを見つけた時点の距離を「元の形」として覚え、そこから変わった所を丸める。
    /// 今のピースの描画は Renderer.forceRenderingOff で隠す。VoxelPiece は作り直しのたびに Renderer.enabled を戻すが、forceRenderingOff には触れない為。
    /// F2 で今の描画との切り替え、F3 で丸みの決め方、F4 で滑らかな位置の元（距離か、中身かどうかだけか）を切り替える。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class BlockSmoothPreview : MonoBehaviour
    {
        private const string CONTAINER_NAME = "BlockSmoothPreview";

        private static readonly int _roundnessOverrideId = Shader.PropertyToID("_BlockRoundnessOverride");

        private readonly List<PieceView> _views = new();
        private readonly List<VoxelPiece> _foundPieces = new();
        private readonly Dictionary<Material, VoxelStrength> _sourceStrengths = new();

        [SerializeField]
        [Tooltip("試作のメッシュを描くシェーダー（Kizami/BuildingPatternBlock）")]
        private Shader _shader;

        [SerializeField]
        [Tooltip("素材の強度を引く為のモデル。ピースのマテリアルの名前が、ここにある素材のマテリアルの名前で始まれば、その強度とみなす")]
        private VoxelModelAsset[] _strengthSources = Array.Empty<VoxelModelAsset>();

        [SerializeField]
        private bool _showPreview = true;

        [SerializeField]
        private RoundnessMode _mode = RoundnessMode.AroundDamage;

        [SerializeField]
        [Tooltip("滑らかな位置と法線を、距離ではなく中身かどうかだけから求める")]
        private bool _useOccupancy;

        [SerializeField, Min(0.01f)]
        [Tooltip("変わった所から丸みが 0 になるまでの距離（m、ピースのローカル空間）")]
        private float _falloffDistance = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("変わったとみなす距離の差（ボクセル数換算）")]
        private float _changeThresholdVoxels = 0.05f;

        [SerializeField, Min(0f)]
        private float _softScale = 1.5f;

        [SerializeField, Min(0f)]
        private float _normalScale = 1f;

        [SerializeField, Min(0f)]
        private float _hardScale = 0.3f;

        [SerializeField, Min(1)]
        [Tooltip("1 フレームで作り直すチャンクの上限数")]
        private int _chunksPerFrame = 64;

        [SerializeField]
        [Tooltip("画面に出す説明の上端の位置")]
        private float _hudTop = 340f;

        private bool _isSettingChanged;
        private int _lastBuildChunkCount;
        private double _lastBuildMilliseconds;
        private double _lastRoundnessMilliseconds;

        /// <summary> 丸みの決め方 </summary>
        private enum RoundnessMode
        {
            /// <summary> 全体を角ばらせる </summary>
            Sharp,

            /// <summary> 全体を滑らかにする </summary>
            Smooth,

            /// <summary> 変わった所の周りだけ滑らかにする </summary>
            AroundDamage
        }

        private float RoundnessOverride => _mode switch
        {
            RoundnessMode.Sharp => 0f,
            RoundnessMode.Smooth => 1f,
            _ => -1f
        };

        private string ModeLabel => _mode switch
        {
            RoundnessMode.Sharp => "全部角ばる",
            RoundnessMode.Smooth => "全部滑らか",
            _ => "壊した周りだけ滑らか"
        };

        private void Awake()
        {
            foreach (var model in _strengthSources)
            {
                if (model == null) continue;

                foreach (var entry in model.Materials)
                {
                    if (entry.Material != null) _sourceStrengths[entry.Material] = entry.Strength;
                }
            }
        }

        private void OnValidate()
        {
            _isSettingChanged = true;
        }

        private void LateUpdate()
        {
            HandleKeys();
            Shader.SetGlobalFloat(_roundnessOverrideId, RoundnessOverride);
            SyncViews();

            foreach (var view in _views)
            {
                if (_isSettingChanged) view.MarkAllDirty();
                view.SetVisible(_showPreview);
            }

            _isSettingChanged = false;
            Rebuild();
        }

        private void OnDestroy()
        {
            foreach (var view in _views)
            {
                view.Dispose();
            }

            _views.Clear();
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(10f, _hudTop, 560f, 44f),
                $"F2: 試作の表示 {(_showPreview ? "あり" : "なし")}  F3: {ModeLabel}  " +
                $"F4: 滑らかな位置の元 {(_useOccupancy ? "中身かどうか" : "距離")}\n" +
                $"直近の作り直し: {_lastBuildChunkCount} チャンク {_lastBuildMilliseconds:0.00} ms" +
                $"（うち丸みの計算 {_lastRoundnessMilliseconds:0.00} ms）");
        }

        private void HandleKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f2Key.wasPressedThisFrame) _showPreview = !_showPreview;

            if (keyboard.f3Key.wasPressedThisFrame) _mode = (RoundnessMode)(((int)_mode + 1) % 3);

            if (keyboard.f4Key.wasPressedThisFrame)
            {
                _useOccupancy = !_useOccupancy;
                _isSettingChanged = true;
            }
        }

        /// <summary>
        /// 子の VoxelPiece と描き直しの対応を合わせる。消えたピースの分は捨て、ボリュームが作り直されたピースは覚え直す。
        /// </summary>
        private void SyncViews()
        {
            GetComponentsInChildren(_foundPieces);

            for (var i = _views.Count - 1; i >= 0; i--)
            {
                var view = _views[i];
                if (view.Piece != null && _foundPieces.Contains(view.Piece) && view.IsVolumeCurrent) continue;

                view.Dispose();
                _views.RemoveAt(i);
            }

            foreach (var piece in _foundPieces)
            {
                if (piece.VolumeData == null || !piece.VolumeData.IsCreated) continue;
                if (_views.Exists(view => view.Piece == piece)) continue;

                _views.Add(new PieceView(piece, CreateMaterials(piece), CreateMaterialScales(piece)));
            }
        }

        /// <summary>
        /// ピースのマテリアルを複製し、シェーダーを試作のものに差し替える。模様のずれなどの値はそのまま引き継ぐ。
        /// </summary>
        private Material[] CreateMaterials(VoxelPiece piece)
        {
            var sources = piece.VolumeData.HasMaterials && piece.Materials.Count > 0
                ? piece.Materials
                : new[] { piece.Material } as IReadOnlyList<Material>;

            var materials = new Material[sources.Count];
            for (var i = 0; i < materials.Length; i++)
            {
                materials[i] = new Material(sources[i]) { name = $"{sources[i].name}_Block" };
                if (_shader != null) materials[i].shader = _shader;
            }

            return materials;
        }

        /// <summary>
        /// ピースの素材の番号ごとの丸みの倍率を作る。強度が分からない素材は普通として扱う。
        /// </summary>
        private float[] CreateMaterialScales(VoxelPiece piece)
        {
            var count = math.max(piece.Materials.Count, 1);
            var scales = new float[count];

            for (var i = 0; i < count; i++)
            {
                var strength = i < piece.Materials.Count ? FindStrength(piece.Materials[i]) : VoxelStrength.Normal;
                scales[i] = strength switch
                {
                    VoxelStrength.Soft => _softScale,
                    VoxelStrength.Hard => _hardScale,
                    _ => _normalScale
                };
            }

            return scales;
        }

        /// <summary>
        /// ピースのマテリアルの強度を、名前の先頭が一致する元の素材から引く。VoxelBuilding が階ごとに複製したマテリアルは「元の名前_FloorNN」になる為。
        /// </summary>
        private VoxelStrength FindStrength(Material material)
        {
            if (material == null) return VoxelStrength.Normal;

            foreach (var (source, strength) in _sourceStrengths)
            {
                if (material.name == source.name || material.name.StartsWith(source.name + "_")) return strength;
            }

            return VoxelStrength.Normal;
        }

        /// <summary>
        /// 丸みを計算し直すピースを計算し、作り直し待ちのチャンクを上限まで並列にメッシュ化する。
        /// </summary>
        private void Rebuild()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var hasRoundnessWork = false;

            foreach (var view in _views)
            {
                if (!view.IsRoundnessDirty) continue;

                view.UpdateRoundness(_falloffDistance, _changeThresholdVoxels);
                hasRoundnessWork = true;
            }

            var roundnessMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            var requests = new List<(PieceView View, int ChunkIndex)>();
            foreach (var view in _views)
            {
                view.DequeueDirtyChunks(_chunksPerFrame - requests.Count, requests);
                if (requests.Count >= _chunksPerFrame) break;
            }

            if (requests.Count == 0 && !hasRoundnessWork) return;

            var buffers = new MeshBuffers[requests.Count];
            var handles = new NativeArray<JobHandle>(requests.Count, Allocator.Temp);
            for (var i = 0; i < requests.Count; i++)
            {
                var (view, chunkIndex) = requests[i];
                buffers[i] = new MeshBuffers(view.MaterialCount, Allocator.TempJob);
                handles[i] = view.ScheduleMeshing(chunkIndex, buffers[i], _useOccupancy);
            }

            JobHandle.CombineDependencies(handles).Complete();
            handles.Dispose();

            for (var i = 0; i < requests.Count; i++)
            {
                var (view, chunkIndex) = requests[i];
                view.ApplyMesh(chunkIndex, buffers[i]);
                buffers[i].Dispose();
            }

            _lastBuildChunkCount = requests.Count;
            _lastBuildMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            _lastRoundnessMilliseconds = roundnessMilliseconds;
        }

        /// <summary>
        /// 1 チャンク分のメッシュ化の出力先。
        /// </summary>
        private struct MeshBuffers : IDisposable
        {
            public NativeList<float3> Vertices;
            public NativeList<float3> Normals;
            public NativeList<float4> SmoothOffsets;
            public NativeList<int> Indices;

            /// <summary> 素材ごとのインデックスの数。Indices は素材の順に並ぶ </summary>
            public NativeArray<int> SubMeshIndexCounts;

            public MeshBuffers(int materialCount, Allocator allocator)
            {
                Vertices = new NativeList<float3>(1024, allocator);
                Normals = new NativeList<float3>(1024, allocator);
                SmoothOffsets = new NativeList<float4>(1024, allocator);
                Indices = new NativeList<int>(4096, allocator);
                SubMeshIndexCounts = new NativeArray<int>(materialCount, allocator);
            }

            public void Dispose()
            {
                Vertices.Dispose();
                Normals.Dispose();
                SmoothOffsets.Dispose();
                Indices.Dispose();
                SubMeshIndexCounts.Dispose();
            }
        }

        /// <summary>
        /// 1 つのピースの描き直し。元の形の距離、点ごとの丸み、チャンクごとの Mesh を持つ。
        /// </summary>
        private sealed class PieceView : IDisposable
        {
            public readonly VoxelPiece Piece;

            private readonly VoxelVolume _volume;
            private readonly Material[] _materials;
            private readonly Transform _container;
            private readonly List<IDisposable> _subscriptions = new();
            private readonly Queue<int> _dirtyQueue = new();
            private readonly bool[] _isDirty;
            private readonly Mesh[] _meshes;

            private NativeArray<float> _initialSamples;
            private NativeArray<float> _roundness;
            private NativeArray<float> _materialScales;
            private NativeArray<byte> _emptyMaterialIds;
            private float _falloffCells = 1f;

            public PieceView(VoxelPiece piece, Material[] materials, float[] materialScales)
            {
                Piece = piece;
                _volume = piece.VolumeData;
                _materials = materials;

                _initialSamples = new NativeArray<float>(_volume.Samples, Allocator.Persistent);
                _roundness = new NativeArray<float>(_volume.Samples.Length, Allocator.Persistent);
                _materialScales = new NativeArray<float>(materialScales, Allocator.Persistent);
                _emptyMaterialIds = new NativeArray<byte>(1, Allocator.Persistent);

                var chunkTotal = _volume.Layout.ChunkTotal;
                _isDirty = new bool[chunkTotal];
                _meshes = new Mesh[chunkTotal];

                var containerObject = new GameObject(CONTAINER_NAME) { layer = piece.gameObject.layer };
                _container = containerObject.transform;
                _container.SetParent(piece.transform, false);

                _subscriptions.Add(piece.RegisterOnShapeChanged(OnShapeChanged));
                _subscriptions.Add(piece.RegisterOnSplit(_ => MarkAllDirty()));
                MarkAllDirty();
            }

            /// <summary> ピースのボリュームが、覚えたときのものから作り直されていないか </summary>
            public bool IsVolumeCurrent => Piece.VolumeData == _volume;

            public bool IsRoundnessDirty { get; private set; }

            public int MaterialCount => _materials.Length;

            private NativeArray<byte> MaterialIds => _volume.HasMaterials ? _volume.MaterialIds : _emptyMaterialIds;

            public void MarkAllDirty()
            {
                IsRoundnessDirty = true;
                for (var i = 0; i < _isDirty.Length; i++)
                {
                    MarkDirty(i);
                }
            }

            /// <summary>
            /// 試作の描画と今の描画のどちらを見せるかを切り替える。今のピースのチャンクは作り直しで増えるので、毎フレーム当て直す。
            /// </summary>
            public void SetVisible(bool showPreview)
            {
                _container.gameObject.SetActive(showPreview);

                foreach (Transform child in Piece.transform)
                {
                    if (child == _container) continue;

                    if (child.TryGetComponent<MeshRenderer>(out var meshRenderer)) meshRenderer.forceRenderingOff = showPreview;
                }
            }

            public void UpdateRoundness(float falloffDistance, float changeThresholdVoxels)
            {
                var layout = _volume.Layout;
                _falloffCells = math.max(falloffDistance / layout.VoxelSize, 1f);

                new BlockSmoothRoundnessJob
                {
                    Layout = layout,
                    ChangeThreshold = changeThresholdVoxels * layout.VoxelSize,
                    Step = 1f / _falloffCells,
                    HasMaterials = _volume.HasMaterials,
                    Samples = _volume.Samples,
                    InitialSamples = _initialSamples,
                    MaterialIds = MaterialIds,
                    MaterialScales = _materialScales,
                    Roundness = _roundness
                }.Run();

                IsRoundnessDirty = false;
            }

            public void DequeueDirtyChunks(int maxCount, List<(PieceView View, int ChunkIndex)> requests)
            {
                for (var i = 0; i < maxCount && _dirtyQueue.Count > 0; i++)
                {
                    var chunkIndex = _dirtyQueue.Dequeue();
                    _isDirty[chunkIndex] = false;
                    requests.Add((this, chunkIndex));
                }
            }

            public JobHandle ScheduleMeshing(int chunkIndex, MeshBuffers buffers, bool useOccupancy)
            {
                var layout = _volume.Layout;
                layout.GetChunkCellRange(layout.ToChunkCoord(chunkIndex), out var cellMin, out var cellMax);

                return new BlockSmoothMeshJob
                {
                    Layout = layout,
                    CellMin = cellMin,
                    CellMax = cellMax,
                    UseOccupancy = useOccupancy,
                    Vertices = buffers.Vertices,
                    Normals = buffers.Normals,
                    SmoothOffsets = buffers.SmoothOffsets,
                    Indices = buffers.Indices,
                    MaterialCount = _materials.Length,
                    SubMeshIndexCounts = buffers.SubMeshIndexCounts,
                    Samples = _volume.Samples,
                    MaterialIds = MaterialIds,
                    Roundness = _roundness
                }.Schedule();
            }

            public void ApplyMesh(int chunkIndex, MeshBuffers buffers)
            {
                if (buffers.Indices.Length == 0)
                {
                    if (_meshes[chunkIndex] != null) _meshes[chunkIndex].Clear();
                    return;
                }

                var mesh = _meshes[chunkIndex] ??= CreateChunk(chunkIndex);
                mesh.Clear();
                mesh.SetVertices(buffers.Vertices.AsArray());
                mesh.SetNormals(buffers.Normals.AsArray());
                mesh.SetUVs(1, buffers.SmoothOffsets.AsArray());

                mesh.subMeshCount = buffers.SubMeshIndexCounts.Length;
                var indices = buffers.Indices.AsArray();
                var start = 0;
                for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    var length = buffers.SubMeshIndexCounts[subMesh];
                    mesh.SetIndices(indices.GetSubArray(start, length), MeshTopology.Triangles, subMesh);
                    start += length;
                }

                // 頂点はシェーダーでセルの中を動くので、境界をボクセル 1 つ分広げて欠けて消えないようにする
                var bounds = mesh.bounds;
                bounds.Expand(_volume.Layout.VoxelSize * 2f);
                mesh.bounds = bounds;
            }

            public void Dispose()
            {
                foreach (var subscription in _subscriptions)
                {
                    subscription.Dispose();
                }

                _subscriptions.Clear();

                if (Piece != null) SetVisible(false);

                if (_container != null) UnityEngine.Object.Destroy(_container.gameObject);

                foreach (var mesh in _meshes)
                {
                    if (mesh != null) UnityEngine.Object.Destroy(mesh);
                }

                foreach (var material in _materials)
                {
                    UnityEngine.Object.Destroy(material);
                }

                _initialSamples.Dispose();
                _roundness.Dispose();
                _materialScales.Dispose();
                _emptyMaterialIds.Dispose();
            }

            private void MarkDirty(int chunkIndex)
            {
                if (_isDirty[chunkIndex]) return;

                _isDirty[chunkIndex] = true;
                _dirtyQueue.Enqueue(chunkIndex);
            }

            /// <summary>
            /// 変わった範囲を丸みが広がる幅だけ広げ、そこを読むチャンクを作り直し待ちにする。
            /// </summary>
            private void OnShapeChanged(VoxelShapeChange change)
            {
                IsRoundnessDirty = true;

                var layout = _volume.Layout;
                var spread = (int)math.ceil(_falloffCells) + 1;
                var sampleMin = (int3)math.floor(layout.ToGridPosition(change.LocalBounds.min)) - spread;
                var sampleMax = (int3)math.ceil(layout.ToGridPosition(change.LocalBounds.max)) + spread;
                sampleMin = math.clamp(sampleMin, 0, layout.SampleCount - 1);
                sampleMax = math.clamp(sampleMax, 0, layout.SampleCount - 1);

                layout.GetChunksAffectedBySamples(sampleMin, sampleMax, out var chunkMin, out var chunkMax);
                for (var z = chunkMin.z; z <= chunkMax.z; z++)
                for (var y = chunkMin.y; y <= chunkMax.y; y++)
                for (var x = chunkMin.x; x <= chunkMax.x; x++)
                {
                    MarkDirty(layout.ToChunkIndex(new int3(x, y, z)));
                }
            }

            private Mesh CreateChunk(int chunkIndex)
            {
                var coord = _volume.Layout.ToChunkCoord(chunkIndex);
                var chunkObject = new GameObject($"BlockChunk_{coord.x}_{coord.y}_{coord.z}")
                {
                    layer = _container.gameObject.layer
                };
                chunkObject.transform.SetParent(_container, false);

                var mesh = new Mesh
                {
                    name = chunkObject.name,
                    indexFormat = IndexFormat.UInt32
                };
                mesh.MarkDynamic();

                chunkObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                chunkObject.AddComponent<MeshRenderer>().sharedMaterials = _materials;
                return mesh;
            }
        }
    }
}
