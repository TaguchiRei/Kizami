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
        /// <summary>
        /// 最大辺の解像度の上限。MeshToSDFBaker は総ボクセル数が 2^27 を超えると例外を投げる。
        /// </summary>
        private const int MaxResolution = 384;

        /// <summary>
        /// ベイクして VoxelModelAsset に保存する。同じパスのアセットがあれば中身を差し替える（GUID は保たれる）。
        /// </summary>
        /// <param name="root">ベイクするモデルのルート。Prefab・モデルアセット・シーン上のインスタンスのいずれでもよい</param>
        /// <param name="settings">ベイク設定</param>
        /// <param name="assetPath">保存先のアセットパス（Assets/～.asset）</param>
        public static VoxelModelAsset BakeAndSave(GameObject root, VoxelBakeSettings settings, string assetPath)
        {
            var parts = Bake(root, settings);

            var asset = AssetDatabase.LoadAssetAtPath<VoxelModelAsset>(assetPath);
            if (asset == null)
            {
                EnsureFolder(assetPath.Substring(0, assetPath.LastIndexOf('/')));
                asset = ScriptableObject.CreateInstance<VoxelModelAsset>();
                asset.SetParts(parts);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                asset.SetParts(parts);
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>
        /// ルート以下の MeshFilter を SDF にベイクする。
        /// パーツごとにベイクする場合、各パーツはその MeshFilter のローカル空間で表す。
        /// まとめる場合は、ルートのローカル空間で表す。
        /// </summary>
        public static VoxelSdfData[] Bake(GameObject root, VoxelBakeSettings settings)
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
                return new[] { BakeCombined(root.transform, filters, settings) };
            }

            var parts = new VoxelSdfData[filters.Count];
            for (var i = 0; i < filters.Count; i++)
            {
                var mesh = filters[i].sharedMesh;
                parts[i] = BakeBox(GetRelativePath(root.transform, filters[i].transform), mesh.bounds, settings,
                    (size, center, resolution) => new MeshToSDFBaker(size, center, resolution, mesh,
                        settings.SignPassCount, settings.InOutThreshold));
            }

            return parts;
        }

        /// <summary>
        /// 全メッシュをルートのローカル空間へ変換し、1 パーツとしてベイクする。
        /// MeshToSDFBaker がメッシュを結合する為、各メッシュの Read/Write が有効である必要がある。
        /// </summary>
        private static VoxelSdfData BakeCombined(Transform root, List<MeshFilter> filters, VoxelBakeSettings settings)
        {
            var meshes = new List<Mesh>(filters.Count);
            var transforms = new List<Matrix4x4>(filters.Count);
            var bounds = new Bounds();

            for (var i = 0; i < filters.Count; i++)
            {
                var toRoot = root.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
                var meshBounds = TransformBounds(filters[i].sharedMesh.bounds, toRoot);

                meshes.Add(filters[i].sharedMesh);
                transforms.Add(toRoot);

                if (i == 0)
                {
                    bounds = meshBounds;
                }
                else
                {
                    bounds.Encapsulate(meshBounds);
                }
            }

            return BakeBox(string.Empty, bounds, settings,
                (size, center, resolution) => new MeshToSDFBaker(size, center, resolution, meshes, transforms,
                    settings.SignPassCount, settings.InOutThreshold));
        }

        /// <summary>
        /// 境界に余白を足した箱で SDF をベイクし、ボクセル中心の距離を量子化して取り出す。
        /// </summary>
        private static VoxelSdfData BakeBox(string path, Bounds bounds, VoxelBakeSettings settings,
            Func<Vector3, Vector3, int, MeshToSDFBaker> createBaker)
        {
            var size = bounds.size + Vector3.one * (settings.PaddingVoxels * settings.VoxelSize * 2f);
            var resolution = Mathf.CeilToInt(Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / settings.VoxelSize);
            if (resolution > MaxResolution)
            {
                Debug.LogWarning($"'{path}' の解像度 {resolution} が上限 {MaxResolution} を超える為、上限に丸めます。");
                resolution = MaxResolution;
            }

            var baker = createBaker(size, bounds.center, resolution);
            try
            {
                baker.BakeSDF();

                var gridSize = baker.GetGridSize();
                var boxSize = baker.GetActualBoxSize();
                var voxelSize = boxSize.x / gridSize.x;

                // MeshToSDFBaker の距離は、箱の最大辺の長さを 1 とした値
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

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            var result = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (var i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                result.Encapsulate(matrix.MultiplyPoint3x4(corner));
            }

            return result;
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
