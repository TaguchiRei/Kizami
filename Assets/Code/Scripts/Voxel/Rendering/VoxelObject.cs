using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kizami.Voxel
{
    /// <summary>
    /// VoxelVolume を保持し、編集されたチャンクのメッシュとコライダーを作り直すコンポーネント。
    ///
    /// ボクセル空間はこの Transform のローカル空間。Transform のスケールは均一である前提。
    /// チャンクは子 GameObject として、初めて面を持ったときに生成する。
    /// 再メッシュ化は LateUpdate で、1 フレームあたり VoxelQualitySettings.RemeshChunksPerFrame 個まで行う。
    /// </summary>
    public sealed class VoxelObject : MonoBehaviour
    {
        private const MeshColliderCookingOptions ColliderCookingOptions =
            MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.UseFastMidphase;

        [SerializeField]
        [Tooltip("ボクセルの大きさ・チャンクの大きさ・再メッシュ化の上限")]
        private VoxelQualitySettings _quality;

        [SerializeField]
        [Tooltip("チャンクの MeshRenderer に割り当てるマテリアル")]
        private Material _material;

        [SerializeField]
        [Tooltip("当たり判定の作り方。動く Rigidbody を付けるなら ConvexHull にする")]
        private VoxelColliderMode _colliderMode = VoxelColliderMode.ChunkMesh;

        private readonly Queue<int> _dirtyQueue = new();

        private VoxelVolume _volume;
        private ChunkSlot[] _chunks;
        private bool[] _isDirty;
        private MeshCollider _hullCollider;
        private Mesh _hullMesh;

        /// <summary> ApplyEdit でボリュームの値が変わったときに、合成方法を渡して呼ばれる </summary>
        public event Action<VoxelCsgOperation> Edited;

        public VoxelQualitySettings Quality => _quality;
        public Material Material => _material;
        public VoxelColliderMode ColliderMode => _colliderMode;
        public VoxelVolume Volume => _volume;

        /// <summary> 再メッシュ化を待っているチャンク数 </summary>
        public int PendingChunkCount => _dirtyQueue.Count;

        /// <summary> 全チャンクの三角形数の合計 </summary>
        public int TriangleCount { get; private set; }

        /// <summary> 直近の再メッシュ化とコライダー更新にかかった時間 </summary>
        public double LastRemeshMilliseconds { get; private set; }

        /// <summary>
        /// 品質設定を差し替える。ボリュームを作る・読み込むより前に呼ぶこと。
        /// </summary>
        public void SetQuality(VoxelQualitySettings quality)
        {
            _quality = quality;
        }

        /// <summary>
        /// チャンクの MeshRenderer に割り当てるマテリアルを差し替える。これ以降に生成するチャンクから反映される。
        /// </summary>
        public void SetMaterial(Material material)
        {
            _material = material;
        }

        /// <summary>
        /// 当たり判定の作り方を差し替える。ボリュームを作る・読み込むより前に呼ぶこと。
        /// </summary>
        public void SetColliderMode(VoxelColliderMode colliderMode)
        {
            _colliderMode = colliderMode;
        }

        /// <summary>
        /// ローカル空間の境界を覆う空のボリュームを作る。既存のボリュームとチャンクは破棄する。
        /// ボクセルの大きさは、品質設定のワールド空間の長さをこの Transform のスケールで割ったもの。
        /// </summary>
        /// <param name="localBounds">ボリュームで覆うローカル空間の範囲</param>
        public void CreateVolume(VoxelBounds localBounds)
        {
            if (!TryCreateLayout(localBounds, out var layout)) return;

            AssignVolume(new VoxelVolume(layout));
        }

        /// <summary>
        /// 事前ベイクした SDF を、品質設定のボクセルの大きさへリサンプルして読み込む。
        /// 既存のボリュームとチャンクは破棄し、全チャンクを再メッシュ化の対象にする。
        /// </summary>
        /// <param name="source">この Transform のローカル空間でベイクした SDF</param>
        public void LoadSdf(VoxelSdfData source)
        {
            if (!TryCreateLayout(source.LocalBounds, out var layout)) return;

            if (source.MaxDistance < layout.VoxelSize * 2f)
            {
                Debug.LogWarning(
                    $"ベイク時の最大距離 {source.MaxDistance} がボクセル 2 つ分 {layout.VoxelSize * 2f} より短い為、法線が荒れます。" +
                    "最大距離を大きくしてベイクし直してください。", this);
            }

            var volume = new VoxelVolume(layout);
            volume.Resample(source);
            LoadVolume(volume);
        }

        /// <summary>
        /// 作成済みのボリュームを読み込む。ボリュームの所有権はこの VoxelObject に移り、破棄もこちらで行う。
        /// 既存のボリュームとチャンクは破棄し、全チャンクを再メッシュ化の対象にする。
        /// </summary>
        /// <param name="volume">この Transform のローカル空間で表したボリューム</param>
        public void LoadVolume(VoxelVolume volume)
        {
            AssignVolume(volume);

            for (var i = 0; i < volume.Layout.ChunkTotal; i++)
            {
                MarkDirty(i);
            }
        }

        /// <summary>
        /// ローカル空間で表した形状をボリュームへ CSG 合成し、影響するチャンクを再メッシュ化の対象にする。
        /// </summary>
        /// <param name="localShape">ローカル空間で表した形状</param>
        /// <param name="operation">埋めるか削るか</param>
        public void ApplyEdit<TShape>(in TShape localShape, VoxelCsgOperation operation)
            where TShape : struct, IVoxelShape
        {
            if (_volume == null)
            {
                Debug.LogError("ボリュームがありません。先に CreateVolume を呼んでください。", this);
                return;
            }

            var result = _volume.ApplyEdit(localShape, operation);
            if (!result.HasChange) return;

            MarkSamplesDirty(result.SampleMin, result.SampleMax);
            Edited?.Invoke(operation);
        }

        /// <summary>
        /// 再メッシュ化を待っている全チャンクを、1 フレームあたりの上限を無視して今すぐ作り直す。
        /// </summary>
        public void FlushRemesh()
        {
            if (_volume == null || _dirtyQueue.Count == 0) return;

            RemeshPending(_dirtyQueue.Count);
        }

        /// <summary>
        /// ワールド空間の長さをローカル空間の長さに変換する。
        /// </summary>
        public float WorldToLocalLength(float worldLength)
        {
            return worldLength / transform.lossyScale.x;
        }

        /// <summary>
        /// 塊のサンプルを外側にし、影響するチャンクを再メッシュ化の対象にする。
        /// </summary>
        /// <param name="labels">VoxelVolume.LabelComponents が振った塊の番号</param>
        /// <param name="component">消す塊</param>
        internal void EraseComponent(NativeArray<int> labels, in VoxelComponent component)
        {
            _volume.EraseComponent(labels, component);
            MarkSamplesDirty(component.SampleMin, component.SampleMax);
        }

        private void Awake()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeVolumeBeforeAssemblyReload;
#endif
        }

        private void LateUpdate()
        {
            if (_volume == null || _dirtyQueue.Count == 0) return;

            RemeshPending(_quality.RemeshChunksPerFrame);
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= DisposeVolumeBeforeAssemblyReload;
#endif
            ReleaseVolume();

            if (_hullMesh != null) Destroy(_hullMesh);
        }

#if UNITY_EDITOR
        /// <summary>
        /// ボリュームの NativeArray を解放する。
        /// 再生中にスクリプトが再コンパイルされると OnDestroy を経ずに C# 側の参照が失われ、解放漏れになる為、
        /// ドメインリロードの直前に呼ぶ。
        /// </summary>
        private void DisposeVolumeBeforeAssemblyReload()
        {
            _volume?.Dispose();
            _volume = null;
        }
#endif

        private bool TryCreateLayout(VoxelBounds localBounds, out VoxelGridLayout layout)
        {
            if (_quality == null)
            {
                Debug.LogError("VoxelQualitySettings が設定されていません。", this);
                layout = default;
                return false;
            }

            layout = VoxelGridLayout.FromBounds(localBounds, WorldToLocalLength(_quality.VoxelSize),
                _quality.ChunkSize);
            return true;
        }

        /// <summary>
        /// 既存のボリュームとチャンクを破棄し、新しいボリュームに差し替える。
        /// </summary>
        private void AssignVolume(VoxelVolume volume)
        {
            ReleaseVolume();
            _volume = volume;
            _chunks = new ChunkSlot[volume.Layout.ChunkTotal];
            _isDirty = new bool[volume.Layout.ChunkTotal];
        }

        /// <summary>
        /// サンプル範囲 [sampleMin, sampleMax] の変更で影響を受けるチャンクを、再メッシュ化の対象にする。
        /// </summary>
        private void MarkSamplesDirty(int3 sampleMin, int3 sampleMax)
        {
            var layout = _volume.Layout;
            layout.GetChunksAffectedBySamples(sampleMin, sampleMax, out var chunkMin, out var chunkMax);

            for (var z = chunkMin.z; z <= chunkMax.z; z++)
            for (var y = chunkMin.y; y <= chunkMax.y; y++)
            for (var x = chunkMin.x; x <= chunkMax.x; x++)
            {
                MarkDirty(layout.ToChunkIndex(new int3(x, y, z)));
            }
        }

        private void MarkDirty(int chunkIndex)
        {
            if (_isDirty[chunkIndex]) return;

            _isDirty[chunkIndex] = true;
            _dirtyQueue.Enqueue(chunkIndex);
        }

        /// <summary>
        /// 待ち行列の先頭から maxCount 個までのチャンクを並列にメッシュ化し、メッシュとコライダーへ反映する。
        /// </summary>
        private void RemeshPending(int maxCount)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var layout = _volume.Layout;
            var useHull = _colliderMode == VoxelColliderMode.ConvexHull;
            var count = math.min(maxCount, _dirtyQueue.Count);
            var chunkIndices = new int[count];
            var buffers = new MeshBuffers[count];
            var handles = new NativeArray<JobHandle>(count, Allocator.Temp);

            for (var i = 0; i < count; i++)
            {
                var chunkIndex = _dirtyQueue.Dequeue();
                _isDirty[chunkIndex] = false;
                chunkIndices[i] = chunkIndex;
                buffers[i] = new MeshBuffers(Allocator.TempJob);

                var meshingHandle = new SurfaceNetsJob
                {
                    Samples = _volume.Samples,
                    Layout = layout,
                    Chunk = layout.ToChunkCoord(chunkIndex),
                    Vertices = buffers[i].Vertices,
                    Normals = buffers[i].Normals,
                    Indices = buffers[i].Indices
                }.Schedule();

                handles[i] = useHull
                    ? new ExtremePointsJob
                    {
                        Points = buffers[i].Vertices.AsDeferredJobArray(),
                        Extremes = buffers[i].Extremes
                    }.Schedule(meshingHandle)
                    : meshingHandle;
            }

            JobHandle.CombineDependencies(handles).Complete();
            handles.Dispose();

            var colliderTargets = new List<ChunkSlot>(count);
            for (var i = 0; i < count; i++)
            {
                var slot = ApplyMesh(chunkIndices[i], buffers[i], useHull);
                if (slot?.Collider != null) colliderTargets.Add(slot);

                buffers[i].Dispose();
            }

            switch (_colliderMode)
            {
                case VoxelColliderMode.ChunkMesh:
                    BakeColliders(colliderTargets);
                    break;

                case VoxelColliderMode.ConvexHull:
                    RebuildHullCollider();
                    break;
            }

            LastRemeshMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// メッシュ化の結果をチャンクへ反映する。
        /// </summary>
        /// <returns>面を持つチャンク。面が無ければ null</returns>
        private ChunkSlot ApplyMesh(int chunkIndex, MeshBuffers buffers, bool useHull)
        {
            var slot = _chunks[chunkIndex];
            var triangleCount = buffers.Indices.Length / 3;

            if (triangleCount == 0)
            {
                if (slot == null) return null;

                TriangleCount -= slot.TriangleCount;
                slot.Clear();
                return null;
            }

            slot ??= _chunks[chunkIndex] = CreateChunkSlot(chunkIndex);
            TriangleCount += triangleCount - slot.TriangleCount;

            var layout = _volume.Layout;
            slot.SetMesh(buffers, layout.GetChunkMeshBounds(layout.ToChunkCoord(chunkIndex)), triangleCount);
            slot.ExtremePoints = useHull ? buffers.Extremes.ToArray() : null;
            return slot;
        }

        private static void BakeColliders(List<ChunkSlot> slots)
        {
            if (slots.Count == 0) return;

            var meshIds = new NativeArray<EntityId>(slots.Count, Allocator.TempJob);
            for (var i = 0; i < slots.Count; i++)
            {
                meshIds[i] = slots[i].Mesh.GetEntityId();
            }

            new BakeColliderJob
            {
                MeshIds = meshIds,
                CookingOptions = ColliderCookingOptions
            }.Schedule(slots.Count, 1).Complete();
            meshIds.Dispose();

            foreach (var slot in slots)
            {
                slot.AssignCollider();
            }
        }

        /// <summary>
        /// 全チャンクの極点から、方向ごとに最も外側の点を選び直し、その点群の凸包をこの GameObject の MeshCollider にする。
        /// 点が 4 つ未満なら当たり判定を無効にする。
        /// </summary>
        private void RebuildHullCollider()
        {
            var directions = new float3[VoxelHullDirections.Count];
            var bestPoints = new float3[VoxelHullDirections.Count];
            var bestDots = new float[VoxelHullDirections.Count];
            for (var d = 0; d < directions.Length; d++)
            {
                directions[d] = VoxelHullDirections.Get(d);
                bestDots[d] = float.NegativeInfinity;
            }

            foreach (var slot in _chunks)
            {
                if (slot?.ExtremePoints == null) continue;

                foreach (var point in slot.ExtremePoints)
                {
                    for (var d = 0; d < directions.Length; d++)
                    {
                        var dot = math.dot(point, directions[d]);
                        if (dot <= bestDots[d]) continue;

                        bestDots[d] = dot;
                        bestPoints[d] = point;
                    }
                }
            }

            var vertices = new List<Vector3>(VoxelHullDirections.Count);
            for (var d = 0; d < bestPoints.Length; d++)
            {
                if (float.IsNegativeInfinity(bestDots[d])) continue;

                Vector3 point = bestPoints[d];
                if (!vertices.Contains(point)) vertices.Add(point);
            }

            if (vertices.Count < 4)
            {
                if (_hullCollider != null) _hullCollider.enabled = false;
                return;
            }

            if (_hullMesh == null) _hullMesh = new Mesh { name = $"{name}_Hull" };
            _hullMesh.Clear();
            _hullMesh.SetVertices(vertices);

            // 凸包の計算は頂点だけを使う。三角形は Mesh として有効にする為の仮のもの
            var triangles = new int[(vertices.Count - 2) * 3];
            for (var i = 0; i < vertices.Count - 2; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            _hullMesh.SetTriangles(triangles, 0);

            if (_hullCollider == null)
            {
                _hullCollider = gameObject.AddComponent<MeshCollider>();
                _hullCollider.convex = true;
            }

            _hullCollider.sharedMesh = null;
            _hullCollider.sharedMesh = _hullMesh;
            _hullCollider.enabled = true;
        }

        private ChunkSlot CreateChunkSlot(int chunkIndex)
        {
            var coord = _volume.Layout.ToChunkCoord(chunkIndex);
            var chunkObject = new GameObject($"VoxelChunk_{coord.x}_{coord.y}_{coord.z}")
            {
                layer = gameObject.layer
            };
            chunkObject.transform.SetParent(transform, false);

            var mesh = new Mesh
            {
                name = chunkObject.name,
                indexFormat = IndexFormat.UInt32
            };
            mesh.MarkDynamic();

            var meshFilter = chunkObject.AddComponent<MeshFilter>();
            var meshRenderer = chunkObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _material;

            // MeshFilter に空の Mesh が入った状態で追加すると、空の Mesh をベイクしようとする為、先に追加する
            MeshCollider meshCollider = null;
            if (_colliderMode == VoxelColliderMode.ChunkMesh)
            {
                meshCollider = chunkObject.AddComponent<MeshCollider>();
                meshCollider.cookingOptions = ColliderCookingOptions;
            }

            meshFilter.sharedMesh = mesh;

            return new ChunkSlot(chunkObject, mesh, meshRenderer, meshCollider);
        }

        private void ReleaseVolume()
        {
            if (_chunks != null)
            {
                foreach (var slot in _chunks)
                {
                    if (slot == null) continue;

                    Destroy(slot.Mesh);
                    Destroy(slot.GameObject);
                }
            }

            if (_hullCollider != null)
            {
                _hullCollider.sharedMesh = null;
                _hullCollider.enabled = false;
            }

            _chunks = null;
            _isDirty = null;
            _dirtyQueue.Clear();
            TriangleCount = 0;

            _volume?.Dispose();
            _volume = null;
        }

        /// <summary>
        /// 1 チャンク分のメッシュ化の出力先。
        /// </summary>
        private struct MeshBuffers : IDisposable
        {
            public NativeList<float3> Vertices;
            public NativeList<float3> Normals;
            public NativeList<int> Indices;

            /// <summary> VoxelHullDirections の方向ごとの、頂点の中で最も外側の点 </summary>
            public NativeArray<float3> Extremes;

            public MeshBuffers(Allocator allocator)
            {
                Vertices = new NativeList<float3>(1024, allocator);
                Normals = new NativeList<float3>(1024, allocator);
                Indices = new NativeList<int>(4096, allocator);
                Extremes = new NativeArray<float3>(VoxelHullDirections.Count, allocator);
            }

            public void Dispose()
            {
                Vertices.Dispose();
                Normals.Dispose();
                Indices.Dispose();
                Extremes.Dispose();
            }
        }

        /// <summary>
        /// 1 チャンク分の GameObject・Mesh・コンポーネント。
        /// </summary>
        private sealed class ChunkSlot
        {
            public readonly GameObject GameObject;
            public readonly Mesh Mesh;
            public readonly MeshRenderer Renderer;
            public readonly MeshCollider Collider;

            public ChunkSlot(GameObject gameObject, Mesh mesh, MeshRenderer renderer, MeshCollider collider)
            {
                GameObject = gameObject;
                Mesh = mesh;
                Renderer = renderer;
                Collider = collider;
            }

            public int TriangleCount { get; private set; }

            /// <summary> 凸包コライダー用の、方向ごとに最も外側の頂点。ConvexHull 以外や面が無いときは null </summary>
            public float3[] ExtremePoints { get; set; }

            public void SetMesh(MeshBuffers buffers, VoxelBounds bounds, int triangleCount)
            {
                Mesh.Clear();
                Mesh.SetVertices(buffers.Vertices.AsArray());
                Mesh.SetNormals(buffers.Normals.AsArray());
                Mesh.SetIndices(buffers.Indices.AsArray(), MeshTopology.Triangles, 0, false);
                Mesh.bounds = new Bounds(bounds.Center, bounds.Size);

                Renderer.enabled = true;
                TriangleCount = triangleCount;
            }

            /// <summary>
            /// ベイク済みの物理メッシュを MeshCollider へ割り当て直す。
            /// </summary>
            public void AssignCollider()
            {
                // 同じ Mesh を代入し直すだけでは物理メッシュが更新されないことがある為、一度外す
                Collider.sharedMesh = null;
                Collider.sharedMesh = Mesh;
                Collider.enabled = true;
            }

            /// <summary>
            /// 面が無くなったチャンクを非表示にし、当たり判定を外す。
            /// </summary>
            public void Clear()
            {
                Mesh.Clear();
                Renderer.enabled = false;
                TriangleCount = 0;
                ExtremePoints = null;

                if (Collider == null) return;

                Collider.sharedMesh = null;
                Collider.enabled = false;
            }
        }
    }
}
