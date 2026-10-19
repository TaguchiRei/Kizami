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
        private const string PROP_FOLDER = "Assets/Level/Prefabs/Stage/Town/Props";

        // 配置を決める乱数のシード。変えると別の街並みになる
        private const int SEED = 14;

        // 敵の経路の格子の範囲。格子は軸に沿った四角形しか取れない為、半径 350m の円を覆う一辺 700m にする
        private const float NAVIGATION_SIZE = 700f;
        private const float NAVIGATION_HEIGHT = 30f;
        private const float NAVIGATION_CENTER_Y = 10f;
        private const float SECTION_SIZE = 100f;

        private const string FACTORY_PREFIX = "EnemyFactory";
        private const string ENEMY_SPAWN_LAYER = "EnemySpawn";
        private const string SQUAD_FOLDER = "Assets/Level/Data/Enemy";

        // 外殻の地面の当たり判定。半径 450〜600m の環を、接線方向に長い箱 8 個で近似する。地面の上面は高さ 0
        private const float FAR_GROUND_RADIUS = 525f;
        private const float FAR_GROUND_LENGTH = 450f;
        private const float FAR_GROUND_WIDTH = 150f;
        private const float FAR_GROUND_THICKNESS = 20f;
        private const int FAR_GROUND_BOXES = 8;

        // チュートリアルのダミーのスポーン位置。東の大通りの上
        private static readonly Vector3 _tutorialDummyPosition = new(135f, 0f, 0f);

        // 地面の当たり判定のレイヤー。倒れた塔の接地用の球は、このレイヤーとだけ接触する
        private const string GROUND_LAYER = "Ground";

        // 奈落のフォグの板を置く高さ（m）
        private const float ABYSS_FOG_HEIGHT = -40f;

        private static readonly string[] _environmentPrefabs =
        {
            "Assets/Level/Prefabs/Stage/Terrain/Stage_Terrain_Island_01.prefab",
            "Assets/Level/Prefabs/Stage/Terrain/Stage_Terrain_AbyssFog_01.prefab",
            "Assets/Level/Prefabs/Stage/Far/Stage_Far_Ring_01.prefab",
            "Assets/Level/Prefabs/Stage/Far/Stage_Far_BarrierDome_01.prefab",
        };

        [MenuItem("Kizami/Stage/Build Town Map")]
        public static void Build()
        {
            var scene = OpenOrCreateScene();
            var old = GameObject.Find(ROOT_NAME);
            if (old != null) Object.DestroyImmediate(old);

            // 塔は以前、環境の 1 枚のメッシュとして置いていた。今は組み立てるたびに置く
            var oldTower = GameObject.Find("Stage_Tower_Tensho_01");
            if (oldTower != null) Object.DestroyImmediate(oldTower);

            // チュートリアルのダミーの区画には施設を置かない。区画の重なりでは先に置いたダミーが使われ、施設から敵が出なくなる為
            var plan = TownLayout.Plan(SEED, SECTION_SIZE, new[] { GetSection(_tutorialDummyPosition) });
            var root = new GameObject(ROOT_NAME).transform;
            var meltSystem = Instantiate($"{BUILDING_FOLDER}/Town_VoxelMeltSystem.prefab", root).GetComponent<VoxelMeltSystem>();

            var tower = Instantiate($"{BUILDING_FOLDER}/Town_TenshoTower.prefab", root);
            SetMeltSystem(tower.GetComponentInChildren<VoxelBuilding>(), meltSystem);
            CreateFarGroundColliders(root);

            var roads = CreateGroup("Roads", root);
            foreach (var road in plan.Roads)
            {
                var instance = Instantiate($"{ROAD_FOLDER}/Stage_Road_{road.Name}_01.prefab", roads);
                Pose(instance, road);
                instance.transform.localScale = new Vector3(1f, 1f, road.LengthScale);
            }

            var spawnSystem = CreateSpawnSystem(root);
            CreateTutorialDummy(spawnSystem);
            var factoryCount = 0;
            var buildings = CreateGroup("Buildings", root);
            foreach (var building in plan.Buildings)
            {
                var instance = Instantiate($"{BUILDING_FOLDER}/Town_{building.Name}.prefab", buildings);
                Pose(instance, building);
                SetMeltSystem(instance.GetComponent<VoxelBuilding>(), meltSystem);
                if (building.Name.StartsWith(FACTORY_PREFIX))
                    SetUpFactory(instance, building.Name, ++factoryCount, spawnSystem);
            }

            foreach (var site in plan.Sites)
                Pose(Instantiate($"{BUILDING_FOLDER}/Town_{site.Name}.prefab", root), site);

            var props = CreateGroup("Props", root);
            foreach (var prop in plan.Props)
                Pose(Instantiate($"{PROP_FOLDER}/Stage_Prop_{prop.Name}_01.prefab", props), prop);

            ApplyGroundLayer();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"街のマップを組み立てました。道路の部品 {plan.Roads.Count} 個、建物 {plan.Buildings.Count} 棟、小物 {plan.Props.Count} 個。");
        }

        /// <summary>
        /// 開いている街のマップの、地面・道・外殻の地面・塔の土台を Ground レイヤーにする。
        /// </summary>
        [MenuItem("Kizami/Stage/Apply Ground Layer")]
        public static void ApplyGroundLayer()
        {
            var layer = LayerMask.NameToLayer(GROUND_LAYER);
            if (layer < 0)
            {
                Debug.LogError($"{GROUND_LAYER} レイヤーがありません。");
                return;
            }

            var count = 0;
            var root = GameObject.Find(ROOT_NAME);
            if (root != null)
            {
                foreach (var name in new[] { "Roads", "FarGroundColliders" })
                {
                    var group = root.transform.Find(name);
                    if (group != null) count += SetLayerRecursively(group.gameObject, layer);
                }

                foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (collider.name.StartsWith("Stage_Tower_Tensho_Lower_01")) count += SetLayerRecursively(collider.gameObject, layer);
                }
            }

            foreach (var island in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (island.name.StartsWith("Stage_Terrain_Island_01")) count += SetLayerRecursively(island.gameObject, layer);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"{count} 個の GameObject を {GROUND_LAYER} レイヤーにしました。");
        }

        private static int SetLayerRecursively(GameObject target, int layer)
        {
            var count = 0;
            foreach (var transform in target.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = layer;
                count++;
            }

            return count;
        }

        private static void SetMeltSystem(VoxelBuilding building, VoxelMeltSystem meltSystem)
        {
            var serialized = new SerializedObject(building);
            serialized.FindProperty("_meltSystem").objectReferenceValue = meltSystem;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 外殻の地面の上に立てるよう、地面の環に沿った箱の当たり判定を置く。壁と遠景のビルには当たり判定を付けない。
        /// </summary>
        private static void CreateFarGroundColliders(Transform root)
        {
            var group = CreateGroup("FarGroundColliders", root);
            for (var i = 0; i < FAR_GROUND_BOXES; i++)
            {
                var angle = i * 360f / FAR_GROUND_BOXES;
                var box = new GameObject($"FarGround_{i}");
                box.transform.SetParent(group, false);
                box.transform.SetLocalPositionAndRotation(
                    Quaternion.Euler(0f, angle, 0f) * new Vector3(FAR_GROUND_RADIUS, -FAR_GROUND_THICKNESS * 0.5f, 0f),
                    Quaternion.Euler(0f, angle, 0f));
                var collider = box.AddComponent<BoxCollider>();
                collider.size = new Vector3(FAR_GROUND_WIDTH, FAR_GROUND_THICKNESS, FAR_GROUND_LENGTH);
            }
        }

        /// <summary>
        /// 位置が入る、EnemySpawnSystem の区画の番号。格子は原点を中心に置くので、区画の中心は SECTION_SIZE の倍数になる。
        /// </summary>
        private static Vector2Int GetSection(Vector3 position)
        {
            return new Vector2Int(Mathf.FloorToInt(position.x / SECTION_SIZE + 0.5f), Mathf.FloorToInt(position.z / SECTION_SIZE + 0.5f));
        }

        private static Transform CreateSpawnSystem(Transform root)
        {
            var system = new GameObject("EnemySpawnSystem");
            system.transform.SetParent(root, false);
            var component = system.AddComponent<EnemySpawnSystem>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("_navigationBounds").boundsValue = new Bounds(
                new Vector3(0f, NAVIGATION_CENTER_Y, 0f), new Vector3(NAVIGATION_SIZE, NAVIGATION_HEIGHT, NAVIGATION_SIZE));
            serialized.FindProperty("_sectionSize").floatValue = SECTION_SIZE;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return system.transform;
        }

        /// <summary>
        /// チュートリアルのダミー（動かない Defender 1 体）のスポーン位置を、出現を保留して置く。台本が ID（Spawn_TutorialDummy）で解放する。
        /// 他のステージでは保留のまま敵を出さない。
        /// </summary>
        private static void CreateTutorialDummy(Transform spawnSystem)
        {
            var spawnObject = new GameObject("SpawnPoint_TutorialDummy");
            spawnObject.transform.SetParent(spawnSystem, false);
            spawnObject.transform.position = _tutorialDummyPosition;
            var spawnPoint = spawnObject.AddComponent<EnemySpawnPoint>();
            spawnObject.AddComponent<StageGimmick>().SetId("Spawn_TutorialDummy");

            var serialized = new SerializedObject(spawnPoint);
            serialized.FindProperty("_composition").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{SQUAD_FOLDER}/EnemySquad_Dummy.asset");
            serialized.FindProperty("_isStationary").boolValue = true;
            serialized.FindProperty("_isHeld").boolValue = true;
            serialized.FindProperty("_radius").floatValue = 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 生産施設に、ギミックの ID・破壊対象・EnemySpawn レイヤーを付け、施設の中にスポーン位置を置く。
        /// 施設の ID は Factory_NN、スポーン位置の ID は Spawn_Factory_NN。
        /// </summary>
        private static void SetUpFactory(GameObject factory, string name, int number, Transform spawnSystem)
        {
            var id = $"Factory_{number:00}";
            factory.name = $"Town_{name}_{number:00}";
            factory.layer = LayerMask.NameToLayer(ENEMY_SPAWN_LAYER);
            factory.AddComponent<StageGimmick>().SetId(id);
            factory.AddComponent<VoxelBuildingDestructionTarget>();

            var isLarge = name.EndsWith("Large");
            var spawnObject = new GameObject($"SpawnPoint_{id}");
            spawnObject.transform.SetParent(spawnSystem, false);
            spawnObject.transform.position = factory.transform.position;
            var spawnPoint = spawnObject.AddComponent<EnemySpawnPoint>();
            spawnObject.AddComponent<StageGimmick>().SetId($"Spawn_{id}");

            var squad = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                $"{SQUAD_FOLDER}/{(isLarge ? "EnemySquad_DefenderFinisher12" : "EnemySquad_Attacker12")}.asset");
            var serialized = new SerializedObject(spawnPoint);
            serialized.FindProperty("_composition").objectReferenceValue = squad;
            serialized.FindProperty("_structureBuilding").objectReferenceValue = factory.GetComponent<VoxelBuilding>();
            serialized.FindProperty("_radius").floatValue = isLarge ? 4f : 2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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

            // プレイヤーのカメラが InGame シーンにある為、既定のカメラは置かない
            var camera = Camera.main;
            if (camera != null) Object.DestroyImmediate(camera.gameObject);
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
