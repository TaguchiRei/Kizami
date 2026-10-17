using Kizami.EngineAdapter;
using Kizami.EngineAdapter.Voxel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kizami.Editor
{
    /// <summary>
    /// 本番の街のマップを TownLayout の配置どおりに組み立て、シーン TownMap に置く。組み立て直すと、生成した物は丸ごと置き換わる。
    /// 建物は、区画の確認シーンで区間を設定した VoxelBuilding のプレハブ（Town_<種類>）を置き、ボクセルの融解の送り先には、生成した物の中に置く VoxelMeltSystem を渡す。
    /// 地形・遠景・ドーム・天照塔は、生成した物とは別に、シーンを作るときに 1 度だけ置く。
    /// </summary>
    public static class TownMapBuilder
    {
        private const string SCENE_PATH = "Assets/Level/Scenes/Town/TownMap.unity";
        private const string ROOT_NAME = "TownMap";
        private const string ROAD_FOLDER = "Assets/Level/Prefabs/Stage/Road";
        private const string BUILDING_FOLDER = "Assets/Level/Prefabs/Stage/Town/Buildings";

        // 配置を決める乱数のシード。変えると別の街並みになる
        private const int SEED = 14;

        // 奈落のフォグの板を置く高さ（m）
        private const float ABYSS_FOG_HEIGHT = -40f;

        private static readonly string[] _environmentPrefabs =
        {
            "Assets/Level/Prefabs/Stage/Terrain/Stage_Terrain_Island_01.prefab",
            "Assets/Level/Prefabs/Stage/Terrain/Stage_Terrain_AbyssFog_01.prefab",
            "Assets/Level/Prefabs/Stage/Far/Stage_Far_Ring_01.prefab",
            "Assets/Level/Prefabs/Stage/Far/Stage_Far_BarrierDome_01.prefab",
            "Assets/Level/Prefabs/Stage/Town/Stage_Tower_Tensho_01.prefab",
        };

        [MenuItem("Kizami/Stage/Build Town Map")]
        public static void Build()
        {
            var scene = OpenOrCreateScene();
            var old = GameObject.Find(ROOT_NAME);
            if (old != null) Object.DestroyImmediate(old);

            var plan = TownLayout.Plan(SEED);
            var root = new GameObject(ROOT_NAME).transform;
            var meltSystem = Instantiate($"{BUILDING_FOLDER}/Town_VoxelMeltSystem.prefab", root).GetComponent<VoxelMeltSystem>();

            var roads = CreateGroup("Roads", root);
            foreach (var road in plan.Roads)
            {
                var instance = Instantiate($"{ROAD_FOLDER}/Stage_Road_{road.Name}_01.prefab", roads);
                Pose(instance, road);
                instance.transform.localScale = new Vector3(1f, 1f, road.LengthScale);
            }

            var buildings = CreateGroup("Buildings", root);
            foreach (var building in plan.Buildings)
            {
                var instance = Instantiate($"{BUILDING_FOLDER}/Town_{building.Name}.prefab", buildings);
                Pose(instance, building);
                var serialized = new SerializedObject(instance.GetComponent<VoxelBuilding>());
                serialized.FindProperty("_meltSystem").objectReferenceValue = meltSystem;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"街のマップを組み立てました。道路の部品 {plan.Roads.Count} 個、建物 {plan.Buildings.Count} 棟。");
        }

        private static UnityEngine.SceneManagement.Scene OpenOrCreateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SCENE_PATH) != null)
                return EditorSceneManager.OpenScene(SCENE_PATH);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var path in _environmentPrefabs)
            {
                var instance = Instantiate(path, null);
                if (path.Contains("AbyssFog")) instance.transform.position = new Vector3(0f, ABYSS_FOG_HEIGHT, 0f);
            }

            // 外壁とドームまで見えるよう、描画距離をマップの外まで延ばす
            var camera = Camera.main;
            camera.farClipPlane = 3000f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 2f, -120f), Quaternion.identity);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            return scene;
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static GameObject Instantiate(string path, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        }

        private static void Pose(GameObject instance, PartPlacement placement)
        {
            instance.transform.SetLocalPositionAndRotation(placement.Position, Quaternion.Euler(0f, placement.Yaw, 0f));
        }
    }
}
