using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kizami.EditorTools
{
    /// <summary>
    /// 選択したモデルのメッシュを、アウトライン用の滑らかな法線を持つ複製に焼き直すエディタのメニュー。
    /// 同じ位置の頂点の法線を、面の角度で重みを付けて平均し、UV の 3 番目のチャンネルに入れる。
    /// 複製は元のモデルと同じフォルダに「メッシュ名_Outline.asset」で作る。
    /// </summary>
    public static class OutlineNormalBaker
    {
        /// <summary> 滑らかな法線を入れる UV のチャンネル。シェーダーの TEXCOORD2 と対応する </summary>
        private const int SMOOTH_NORMAL_CHANNEL = 2;

        private const string MENU_PATH = "Assets/Kizami/Bake Outline Normals";
        private const string OUTPUT_SUFFIX = "_Outline";

        /// <summary> 同じ位置の頂点とみなす量子化の細かさ。1 / この値より近い頂点は同じ位置にする </summary>
        private const float POSITION_PRECISION = 10000f;

        [MenuItem(MENU_PATH, true)]
        private static bool ValidateBake()
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is ModelImporter) return true;
            }

            return false;
        }

        /// <summary>
        /// モデルに含まれる全てのメッシュを焼き直して、同じフォルダに保存する。
        /// </summary>
        /// <param name="modelPath">モデルのアセットパス</param>
        public static void BakeModel(string modelPath)
        {
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter) return;

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is Mesh mesh) BakeMesh(mesh, Path.GetDirectoryName(modelPath));
            }

            AssetDatabase.SaveAssets();
        }

        [MenuItem(MENU_PATH)]
        private static void Bake()
        {
            foreach (var guid in Selection.assetGUIDs) BakeModel(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static void BakeMesh(Mesh source, string directory)
        {
            var baked = Object.Instantiate(source);
            baked.name = source.name + OUTPUT_SUFFIX;
            baked.SetUVs(SMOOTH_NORMAL_CHANNEL, new List<Vector3>(CalculateSmoothNormals(source)));

            // 既にあるアセットは中身だけ差し替えて、参照が切れないように GUID を保つ
            var outputPath = $"{directory}/{baked.name}.asset".Replace('\\', '/');
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(baked, existing);
                Object.DestroyImmediate(baked);
            }
            else
            {
                AssetDatabase.CreateAsset(baked, outputPath);
            }
        }

        private static Vector3[] CalculateSmoothNormals(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var sums = new Dictionary<Vector3Int, Vector3>();

            for (var i = 0; i < triangles.Length; i += 3)
            {
                var faceNormal = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]).normalized;

                for (var corner = 0; corner < 3; corner++)
                {
                    var current = vertices[triangles[i + corner]];
                    var next = vertices[triangles[i + (corner + 1) % 3]];
                    var previous = vertices[triangles[i + (corner + 2) % 3]];
                    var angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot((next - current).normalized, (previous - current).normalized), -1f, 1f));

                    var key = Quantize(current);
                    sums.TryGetValue(key, out var sum);
                    sums[key] = sum + faceNormal * angle;
                }
            }

            var normals = mesh.normals;
            var result = new Vector3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var sum = sums[Quantize(vertices[i])];
                result[i] = sum.sqrMagnitude > 0f ? sum.normalized : normals[i];
            }

            return result;
        }

        private static Vector3Int Quantize(Vector3 position)
        {
            return new Vector3Int(
                Mathf.RoundToInt(position.x * POSITION_PRECISION),
                Mathf.RoundToInt(position.y * POSITION_PRECISION),
                Mathf.RoundToInt(position.z * POSITION_PRECISION));
        }
    }
}
