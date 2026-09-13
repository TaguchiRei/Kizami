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

        private readonly Queue<int> _dirtyQueue = new();

        private VoxelVolume _volume;
        private ChunkSlot[] _chunks;
        private bool[] _isDirty;

        public VoxelQualitySettings Quality => _quality;
        public VoxelVolume Volume => _volume;

        /// <summary> 再メッシュ化を待っているチャンク数 </summary>
        public int PendingChunkCount => _dirtyQueue.Count;

        /// <summary> 全チャンクの三角形数の合計 </summary>
        public int TriangleCount { get; private set; }

        /// <summary> 直近の LateUpdate で再メッシュ化とコライダー更新にかかった時間 </summary>
        public double LastRemeshMilliseconds { get; private set; }

        /// <summary>
        /// 品質設定を差し替える。CreateVolume より前に呼ぶこと。
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
            AssignVolume(volume);

            for (var i = 0; i < layout.ChunkTotal; i++)
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

            var layout = _volume.Layout;
            layout.GetChunksAffectedBySamples(result.SampleMin, result.SampleMax, out var chunkMin, out var chunkMax);

            for (var z = chunkMin.z; z <= chunkMax.z; z++)
            for (var y = chunkMin.y; y <= chunkMax.y; y++)
            for (var x = chunkMin.x; x <= chunkMax.x; x++)
            {
                MarkDirty(layout.ToChunkIndex(new int3(x, y, z)));
            }
        }

        /// <summary>
        /// ワールド空間の長さをローカル空間の長さに変換する。
        /// </summary>
        public float WorldToLocalLength(float worldLength)
        {
            return worldLength / transform.lossyScale.x;
        }

        private void LateUpdate()
        {
            if (_volume == null || _dirtyQueue.Count == 0) return;

            RemeshPending();
        }

        private void OnDestroy()
        {
            ReleaseVolume();
        }

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

        private void MarkDirty(int chunkIndex)
        {
            if (_isDirty[chunkIndex]) return;

            _isDirty[chunkIndex] = true;
            _dirtyQueue.Enqueue(chunkIndex);
        }

        /// <summary>
        /// 待ち行列の先頭から上限数までのチャンクを並列にメッシュ化し、メッシュとコライダーへ反映する。
        /// </summary>
        private void RemeshPending()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var layout = _volume.Layout;
            var count = math.min(_quality.RemeshChunksPerFrame, _dirtyQueue.Count);
            var chunkIndices = new int[count];
            var buffers = new MeshBuffers[count];
            var handles = new NativeArray<JobHandle>(count, Allocator.Temp);

            for (var i = 0; i < count; i++)
            {
                var chunkIndex = _dirtyQueue.Dequeue();
                _isDirty[chunkIndex] = false;
                chunkIndices[i] = chunkIndex;
                buffers[i] = new MeshBuffers(Allocator.TempJob);

                handles[i] = new SurfaceNetsJob
                {
                    Samples = _volume.Samples,
                    Layout = layout,
                    Chunk = layout.ToChunkCoord(chunkIndex),
                    Vertices = buffers[i].Vertices,
                    Normals = buffers[i].Normals,
                    Indices = buffers[i].Indices
                }.Schedule();
            }

            JobHandle.CombineDependencies(handles).Complete();
            handles.Dispose();

            var colliderTargets = new List<ChunkSlot>(count);
            for (var i = 0; i < count; i++)
            {
                var slot = ApplyMesh(chunkIndices[i], buffers[i]);
                if (slot?.Collider != null) colliderTargets.Add(slot);

                buffers[i].Dispose();
            }

            BakeColliders(colliderTargets);

            LastRemeshMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// メッシュ化の結果をチャンクへ反映する。
        /// </summary>
        /// <returns>面を持つチャンク。面が無ければ null</returns>
        private ChunkSlot ApplyMesh(int chunkIndex, MeshBuffers buffers)
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
            if (_quality.GenerateColliders)
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

            public MeshBuffers(Allocator allocator)
            {
                Vertices = new NativeList<float3>(1024, allocator);
                Normals = new NativeList<float3>(1024, allocator);
                Indices = new NativeList<int>(4096, allocator);
            }

            public void Dispose()
            {
                Vertices.Dispose();
                Normals.Dispose();
                Indices.Dispose();
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

                if (Collider == null) return;

                Collider.sharedMesh = null;
                Collider.enabled = false;
            }
        }
    }
}
