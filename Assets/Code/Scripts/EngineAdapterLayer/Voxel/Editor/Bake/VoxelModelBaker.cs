using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX.SDF;

namespace Kizami.Editor.Voxel
{
    /// <summary>
    /// モデルの MeshFilter を、VFX Graph の MeshToSDFBaker（GPU）で SDF にベイクして VoxelModelAsset に保存する。
    /// </summary>
    public static class VoxelModelBaker
    {
        /// <summary> 最大辺の解像度の上限。MeshToSDFBaker は総ボクセル数が2^27を超えると例外を投げる </summary>
        private const int MAX_RESOLUTION = 384;

        /// <summary> 素材の番号を1バイトで持つ為の、素材の数の上限 </summary>
        private const int MAX_MATERIAL_COUNT = 256;

        /// <summary>
        /// ベイクに渡す三角形の辺の長さの上限（ベイクのボクセル数）。
        /// MeshToSDFBaker は大きな三角形を含むと内外の判定を誤り、平たく大きい箱の中身の一部を外側にする為、これより長い辺は割る。
        /// 細かく割りすぎると、部材どうしが接する面から外側へ筋状に内側の点が漏れる（上限を8にすると漏れる）。
        /// </summary>
        private const int MAX_EDGE_VOXELS = 16;

        /// <summary>
        /// ベイクして VoxelModelAsset に保存する。同じパスのアセットがあれば中身を差し替える（GUID は保たれる）。
        /// </summary>
        /// <param name="root">ベイクするモデルのルート。Prefab・モデルアセット・シーン上のインスタンスのいずれでもよい</param>
        /// <param name="settings">ベイク設定</param>
        /// <param name="assetPath">保存先のアセットパス（Assets/～.asset）</param>
        public static VoxelModelAsset BakeAndSave(GameObject root, VoxelBakeSettings settings, string assetPath)
        {
            var parts = Bake(root, settings, out var materials);

            var asset = AssetDatabase.LoadAssetAtPath<VoxelModelAsset>(assetPath);
            if (asset == null)
            {
                EnsureFolder(assetPath.Substring(0, assetPath.LastIndexOf('/')));
                asset = ScriptableObject.CreateInstance<VoxelModelAsset>();
                asset.SetParts(parts, materials);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                asset.SetParts(parts, materials);
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>
        /// ルート以下の MeshFilter を SDF にベイクする。
        /// パーツごとにベイクする場合、各パーツはその MeshFilter のローカル空間で表す。
        /// まとめる場合は、ルートのローカル空間で表し、サンプルごとに素材の番号を書く。
        /// </summary>
        /// <param name="materials">素材の番号が指す素材の一覧。パーツごとにベイクする場合は空</param>
        public static VoxelSdfData[] Bake(GameObject root, VoxelBakeSettings settings, out VoxelMaterialEntry[] materials)
        {
            var filters = new List<MeshFilter>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null) filters.Add(filter);
            }

            if (filters.Count == 0)
            {
                throw new ArgumentException($"{root.name} の階層に Mesh を持つ MeshFilter がありません。");
            }

            if (settings.CombineHierarchy)
            {
                var createdMeshes = new List<Mesh>();
                try
                {
                    return new[] { BakeCombined(root.transform, filters, settings, createdMeshes, out materials) };
                }
                finally
                {
                    foreach (var mesh in createdMeshes)
                    {
                        UnityEngine.Object.DestroyImmediate(mesh);
                    }
                }
            }

            materials = Array.Empty<VoxelMaterialEntry>();

            var parts = new VoxelSdfData[filters.Count];
            for (var i = 0; i < filters.Count; i++)
            {
                var mesh = Subdivide(filters[i].sharedMesh, Matrix4x4.identity, settings.VoxelSize * MAX_EDGE_VOXELS);
                try
                {
                    parts[i] = BakeBox(GetRelativePath(root.transform, filters[i].transform), mesh.bounds, settings,
                        (size, center, resolution) => new MeshToSDFBaker(size, center, resolution, mesh,
                            settings.SignPassCount, settings.InOutThreshold));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }

            return parts;
        }

        /// <summary>
        /// 全メッシュをルートのローカル空間へ変換し、1パーツとしてベイクする。
        /// パーツをマテリアルと強度の組ごとに分け、組ごとにも同じ箱と解像度でベイクする。
        /// 各サンプルには、距離が最も小さい組（そのサンプルを含む組、どれにも含まれなければ最も近い組）の番号を書く。
        /// MeshToSDFBaker がメッシュを結合する為、各メッシュの Read/Write が有効である必要がある。
        /// </summary>
        /// <param name="createdMeshes">ベイクの為に作ったメッシュを足す一覧。呼び出し側で破棄する</param>
        private static VoxelSdfData BakeCombined(Transform root, List<MeshFilter> filters, VoxelBakeSettings settings,
            List<Mesh> createdMeshes, out VoxelMaterialEntry[] materials)
        {
            var meshes = new List<Mesh>(filters.Count);
            var transforms = new List<Matrix4x4>(filters.Count);
            var materialList = new List<VoxelMaterialEntry>();
            var groupMeshes = new List<List<Mesh>>();
            var groupTransforms = new List<List<Matrix4x4>>();
            var bounds = new Bounds();

            for (var i = 0; i < filters.Count; i++)
            {
                // 引き伸ばした箱の三角形はルートの空間で長くなる為、ルートの空間へ移してから割る
                var toRoot = root.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
                var rootMesh = Subdivide(filters[i].sharedMesh, toRoot, settings.VoxelSize * MAX_EDGE_VOXELS);
                createdMeshes.Add(rootMesh);
                var meshBounds = rootMesh.bounds;

                meshes.Add(rootMesh);
                transforms.Add(Matrix4x4.identity);

                var entry = GetMaterialEntry(filters[i]);
                var group = materialList.IndexOf(entry);
                if (group < 0)
                {
                    group = materialList.Count;
                    materialList.Add(entry);
                    groupMeshes.Add(new List<Mesh>());
                    groupTransforms.Add(new List<Matrix4x4>());
                }

                groupMeshes[group].Add(rootMesh);
                groupTransforms[group].Add(Matrix4x4.identity);

                if (i == 0)
                {
                    bounds = meshBounds;
                }
                else
                {
                    bounds.Encapsulate(meshBounds);
                }
            }

            if (materialList.Count > MAX_MATERIAL_COUNT)
            {
                throw new ArgumentException(
                    $"{root.name} の素材の数 {materialList.Count} が上限 {MAX_MATERIAL_COUNT} を超えています。");
            }

            var combined = BakeBox(string.Empty, bounds, settings,
                (size, center, resolution) => new MeshToSDFBaker(size, center, resolution, meshes, transforms,
                    settings.SignPassCount, settings.InOutThreshold));

            materials = materialList.ToArray();
            var materialIds = new byte[combined.Samples.Length];
            if (materialList.Count == 1) return WithMaterialIds(combined, materialIds);

            var nearest = new short[combined.Samples.Length];
            for (var group = 0; group < materialList.Count; group++)
            {
                var groupMeshList = groupMeshes[group];
                var groupTransformList = groupTransforms[group];
                var groupData = BakeBox(string.Empty, bounds, settings,
                    (size, center, resolution) => new MeshToSDFBaker(size, center, resolution, groupMeshList,
                        groupTransformList, settings.SignPassCount, settings.InOutThreshold));
                if (!groupData.SampleCount.Equals(combined.SampleCount))
                {
                    throw new InvalidOperationException("素材ごとのベイクの格子が、全体の格子と一致しません。");
                }

                var groupSamples = groupData.Samples;
                for (var i = 0; i < groupSamples.Length; i++)
                {
                    if (group > 0 && groupSamples[i] >= nearest[i]) continue;

                    nearest[i] = groupSamples[i];
                    materialIds[i] = (byte)group;
                }
            }

            return WithMaterialIds(combined, materialIds);
        }

        /// <summary>
        /// パーツの MeshRenderer のマテリアルと、VoxelBakeMaterial の強度（無ければ普通）の組。
        /// </summary>
        private static VoxelMaterialEntry GetMaterialEntry(MeshFilter filter)
        {
            var material = filter.TryGetComponent<MeshRenderer>(out var meshRenderer) ? meshRenderer.sharedMaterial : null;
            var strength = filter.TryGetComponent<VoxelBakeMaterial>(out var bakeMaterial)
                ? bakeMaterial.Strength
                : VoxelStrength.Normal;
            return new VoxelMaterialEntry(material, strength);
        }

        private static VoxelSdfData WithMaterialIds(VoxelSdfData data, byte[] materialIds)
        {
            var count = data.SampleCount;
            return new VoxelSdfData(data.Path, data.Origin, new Vector3Int(count.x, count.y, count.z), data.VoxelSize,
                data.MaxDistance, data.Samples, materialIds);
        }

        /// <summary>
        /// 境界に余白を足した箱で SDF をベイクし、ボクセル中心の距離を量子化して取り出す。
        /// </summary>
        private static VoxelSdfData BakeBox(string path, Bounds bounds, VoxelBakeSettings settings,
            Func<Vector3, Vector3, int, MeshToSDFBaker> createBaker)
        {
            var size = bounds.size + Vector3.one * (settings.PaddingVoxels * settings.VoxelSize * 2f);
            var resolution = Mathf.CeilToInt(Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / settings.VoxelSize);
            if (resolution > MAX_RESOLUTION)
            {
                Debug.LogWarning($"'{path}' の解像度 {resolution} が上限 {MAX_RESOLUTION} を超える為、上限に丸めます。");
                resolution = MAX_RESOLUTION;
            }

            var baker = createBaker(size, bounds.center, resolution);
            try
            {
                baker.BakeSDF();

                var gridSize = baker.GetGridSize();
                var boxSize = baker.GetActualBoxSize();
                var voxelSize = boxSize.x / gridSize.x;

                // MeshToSDFBaker の距離は、箱の最大辺の長さを1とした値
                var distanceScale = voxelSize * Mathf.Max(gridSize.x, Mathf.Max(gridSize.y, gridSize.z));
                var samples = ReadDistances(baker.SdfTexture, gridSize, distanceScale, settings.MaxDistance);

                // MeshToSDFBaker のサンプルはボクセルの中心にある
                var origin = bounds.center - boxSize * 0.5f + Vector3.one * (voxelSize * 0.5f);

                return new VoxelSdfData(path, origin, gridSize, voxelSize, settings.MaxDistance, samples);
            }
            finally
            {
                baker.Dispose();
            }
        }

        /// <summary>
        /// 3D テクスチャ（R16_SFloat）を読み戻し、ローカル空間の距離に直して量子化する。
        /// </summary>
        private static short[] ReadDistances(RenderTexture texture, Vector3Int gridSize, float distanceScale,
            float maxDistance)
        {
            var request = AsyncGPUReadback.Request(texture, 0);
            request.WaitForCompletion();
            if (request.hasError)
            {
                throw new InvalidOperationException("SDF テクスチャの読み戻しに失敗しました。");
            }

            var sliceLength = gridSize.x * gridSize.y;
            var samples = new short[sliceLength * gridSize.z];

            for (var z = 0; z < gridSize.z; z++)
            {
                var slice = request.GetData<ushort>(z);
                for (var i = 0; i < sliceLength; i++)
                {
                    var distance = Mathf.HalfToFloat(slice[i]) * distanceScale;
                    samples[z * sliceLength + i] = VoxelSdfEncoding.Quantize(distance, maxDistance);
                }
            }

            return samples;
        }

        /// <summary>
        /// メッシュの頂点を matrix で移し、辺が maxEdge より長い三角形を、一番長い辺の中点で割っていった新しいメッシュを作る。
        /// 呼び出し側で破棄する。
        /// </summary>
        private static Mesh Subdivide(Mesh source, Matrix4x4 matrix, float maxEdge)
        {
            var vertices = new List<Vector3>(source.vertexCount);
            foreach (var vertex in source.vertices)
            {
                vertices.Add(matrix.MultiplyPoint3x4(vertex));
            }

            var midpoints = new Dictionary<(int, int), int>();
            var triangles = new List<int>();
            var sourceTriangles = source.triangles;
            var sqrMaxEdge = maxEdge * maxEdge;
            var stack = new Stack<(int, int, int)>();

            for (var i = 0; i < sourceTriangles.Length; i += 3)
            {
                stack.Push((sourceTriangles[i], sourceTriangles[i + 1], sourceTriangles[i + 2]));
                while (stack.Count > 0)
                {
                    var (a, b, c) = stack.Pop();
                    var ab = (vertices[a] - vertices[b]).sqrMagnitude;
                    var bc = (vertices[b] - vertices[c]).sqrMagnitude;
                    var ca = (vertices[c] - vertices[a]).sqrMagnitude;
                    if (ab <= sqrMaxEdge && bc <= sqrMaxEdge && ca <= sqrMaxEdge)
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                        continue;
                    }

                    // 一番長い辺を a-b に回してから割り、向き（巻き順）を保つ
                    if (bc >= ab && bc >= ca) (a, b, c) = (b, c, a);
                    else if (ca >= ab && ca >= bc) (a, b, c) = (c, a, b);

                    var m = GetMidpoint(vertices, midpoints, a, b);
                    stack.Push((a, m, c));
                    stack.Push((m, b, c));
                }
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int GetMidpoint(List<Vector3> vertices, Dictionary<(int, int), int> midpoints, int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (midpoints.TryGetValue(key, out var index)) return index;

            index = vertices.Count;
            vertices.Add((vertices[a] + vertices[b]) * 0.5f);
            midpoints.Add(key, index);
            return index;
        }

        /// <summary>
        /// root から target までの Transform.Find 用のパス。target が root なら空文字。
        /// </summary>
        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return string.Empty;

            var path = target.name;
            for (var current = target.parent; current != null && current != root; current = current.parent)
            {
                path = current.name + "/" + path;
            }

            return path;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
        }
    }
}
