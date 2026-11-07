using Kizami.EngineAdapter;
using UnityEditor;
using UnityEngine;

namespace Kizami.Editor
{
    /// <summary>
    /// 塔の上の部分（Stage_Tower_Tensho_Upper_01）のプレハブに、倒れて橋になったときに敵を出すスポーン位置を、4 つの側面へ置く。
    /// 面ごとに TowerFace を置き、その子に、塔の高さ方向に一定の間隔で、固定する敵（Attacker 1 体）のスポーン位置を並べる。
    /// スポーン位置は出現を保留し、構造物の上に置く設定にする。出したあとは保留に戻るので、倒しても出し直さない。置き直すと、前に置いた面は消えて作り直す。
    /// </summary>
    public static class TowerBridgeSpawnBuilder
    {
        private const string UPPER_PREFAB = "Assets/Level/Prefabs/Stage/Town/Stage_Tower_Tensho_Upper_01.prefab";
        private const string SQUAD_PATH = "Assets/Level/Data/Enemy/EnemySquad_Bridge.asset";

        // 点を置く高さの範囲と間隔（m）。Upper の原点は Cap の底面で、上の部分は 4m から始まる
        private const float FIRST_HEIGHT = 120f;
        private const float LAST_HEIGHT = 720f;
        private const float SPACING = 80f;

        // 面の上から点を浮かせる高さ（m）
        private const float SURFACE_OFFSET = 0.3f;

        // 高さごとの塔の断面の半幅（m）。モデルの頂点から測った値。間は直線でつなぐ
        private static readonly Vector2[] _halfWidths =
        {
            new(0f, 20f), new(60f, 16f), new(120f, 15f), new(360f, 12f), new(760f, 8f),
        };

        private static readonly Vector3[] _normals =
        {
            Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
        };

        [MenuItem("Kizami/Stage/Build Tower Bridge Spawns")]
        public static void Build()
        {
            var squad = LoadOrCreateSquad();
            var root = PrefabUtility.LoadPrefabContents(UPPER_PREFAB);
            foreach (var old in root.GetComponentsInChildren<TowerFace>(true))
                Object.DestroyImmediate(old.gameObject);

            var pointCount = 0;
            foreach (var normal in _normals)
            {
                var face = new GameObject($"Face_{FaceName(normal)}");
                face.transform.SetParent(root.transform, false);
                face.AddComponent<TowerFace>().SetNormal(normal);

                for (var height = FIRST_HEIGHT; height <= LAST_HEIGHT; height += SPACING)
                {
                    var point = new GameObject($"SpawnPoint_{FaceName(normal)}_{height:0}");
                    point.transform.SetParent(face.transform, false);
                    point.transform.localPosition = normal * (GetHalfWidth(height) + SURFACE_OFFSET) + Vector3.up * height;
                    SetUpSpawnPoint(point.AddComponent<EnemySpawnPoint>(), squad);
                    pointCount++;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, UPPER_PREFAB);
            PrefabUtility.UnloadPrefabContents(root);
            Debug.Log($"塔の上の部分に、4 つの面と {pointCount} 個のスポーン位置を置きました。");
        }

        private static string FaceName(Vector3 normal)
        {
            if (normal == Vector3.right) return "PlusX";
            if (normal == Vector3.left) return "MinusX";
            return normal == Vector3.forward ? "PlusZ" : "MinusZ";
        }

        private static float GetHalfWidth(float height)
        {
            for (var i = 1; i < _halfWidths.Length; i++)
            {
                if (height > _halfWidths[i].x) continue;

                var t = Mathf.InverseLerp(_halfWidths[i - 1].x, _halfWidths[i].x, height);
                return Mathf.Lerp(_halfWidths[i - 1].y, _halfWidths[i].y, t);
            }

            return _halfWidths[^1].y;
        }

        private static void SetUpSpawnPoint(EnemySpawnPoint spawnPoint, EnemySquadComposition squad)
        {
            var serialized = new SerializedObject(spawnPoint);
            serialized.FindProperty("_composition").objectReferenceValue = squad;
            serialized.FindProperty("_isOnStructure").boolValue = true;
            serialized.FindProperty("_isPinned").boolValue = true;
            serialized.FindProperty("_isStationary").boolValue = true;
            serialized.FindProperty("_isHeld").boolValue = true;
            serialized.FindProperty("_radius").floatValue = 0f;
            serialized.FindProperty("_respawnInterval").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static EnemySquadComposition LoadOrCreateSquad()
        {
            var squad = AssetDatabase.LoadAssetAtPath<EnemySquadComposition>(SQUAD_PATH);
            if (squad != null) return squad;

            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<EnemySquadComposition>(), SQUAD_PATH);
            AssetDatabase.SaveAssets();
            squad = AssetDatabase.LoadAssetAtPath<EnemySquadComposition>(SQUAD_PATH);
            var serialized = new SerializedObject(squad);
            var members = serialized.FindProperty("_members");
            members.arraySize = 1;
            members.GetArrayElementAtIndex(0).enumValueIndex = (int)EnemyKind.Attacker;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(squad);
            AssetDatabase.SaveAssets();
            return squad;
        }
    }
}
