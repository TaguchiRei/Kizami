using System;
using System.IO;
using Kizami.EngineAdapter;
using UnityEditor;
using UnityEngine;

namespace Kizami.Editor
{
    /// <summary>
    /// 建物の生成スクリプトが FBX の隣に書き出す `<モデル名>_Glass.json` から、ガラスの板を並べた付属物のプレハブを作る。
    /// 作ったプレハブは VoxelBuilding の階ごとの付属物に設定する。
    /// 板は元の板のプレハブを並べ、幅と高さに合わせて拡大する。元の板の大きさは、割れる前の板のメッシュの範囲から測る。
    /// </summary>
    public static class GlassAttachmentBuilder
    {
        private const string LAYOUT_FOLDER = "Assets/Art/Models/Stage";
        private const string LAYOUT_SUFFIX = "_Glass.json";
        private const string OUTPUT_FOLDER = "Assets/Level/Prefabs/Stage/Town";
        private const string PANE_PREFAB_PATH = OUTPUT_FOLDER + "/Stage_Glass_Pane_01.prefab";

        [MenuItem("Kizami/Stage/Build Glass Attachments")]
        public static void BuildAll()
        {
            var panePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PANE_PREFAB_PATH);
            if (panePrefab == null)
            {
                Debug.LogError($"ガラスの板のプレハブが見つかりません: {PANE_PREFAB_PATH}");
                return;
            }

            var paneSize = MeasurePane(panePrefab);
            var built = 0;
            foreach (var path in Directory.GetFiles(LAYOUT_FOLDER, "*" + LAYOUT_SUFFIX))
            {
                var modelName = Path.GetFileName(path)[..^LAYOUT_SUFFIX.Length];
                var layout = JsonUtility.FromJson<GlassLayout>(File.ReadAllText(path));
                Build($"{modelName}_Attachments", layout, panePrefab, paneSize);
                built++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"ガラスの付属物のプレハブを {built} 個作りました。");
        }

        private static void Build(string name, GlassLayout layout, GameObject panePrefab, Vector2 paneSize)
        {
            var root = new GameObject(name);
            try
            {
                foreach (var pane in layout.Panes)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(panePrefab, root.transform);
                    instance.transform.SetLocalPositionAndRotation(
                        new Vector3(pane.Position[0], pane.Position[1], pane.Position[2]),
                        Quaternion.Euler(0f, pane.Yaw, 0f));
                    instance.transform.localScale = new Vector3(pane.Size[0] / paneSize.x, pane.Size[1] / paneSize.y, 1f);
                }

                PrefabUtility.SaveAsPrefabAsset(root, $"{OUTPUT_FOLDER}/{name}.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 割れる前の板のメッシュの幅と高さ。
        /// </summary>
        private static Vector2 MeasurePane(GameObject panePrefab)
        {
            var intact = panePrefab.GetComponentInChildren<MeshFilter>();
            var size = intact.sharedMesh.bounds.size;
            return new Vector2(size.x, size.y);
        }

        [Serializable]
        private sealed class GlassLayout
        {
            public PaneLayout[] Panes;
        }

        /// <summary> 生成スクリプトが書き出す板1枚の配置。名前は JSON のキーに合わせる </summary>
        [Serializable]
        private sealed class PaneLayout
        {
            public float[] Position;
            public float[] Size;
            public float Yaw;
        }
    }
}
