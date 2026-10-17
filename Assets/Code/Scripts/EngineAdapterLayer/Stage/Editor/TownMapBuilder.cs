using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kizami.Editor
{
    /// <summary>
    /// 本番の街のマップを、ゾーンの規則から組み立ててシーン TownMap に置く。組み立て直すと、生成した物は丸ごと置き換わる。
    /// 道は大通り（中央ロータリーから東西南北へ）と、X・Z が ±100 m の中通りの線で、どちらも崖の手前まで通す。
    /// 線どうしの交点には、幅の広いほうの十字路を置き、その間を長さ 16 m の直線で埋める。最後の 1 枚に端数が出ないよう、区間の直線は同じ倍率で道の向きに伸び縮みさせる。
    /// 地形・遠景・ドーム・天照塔は、生成した物とは別に、シーンを作るときに 1 度だけ置く。
    /// </summary>
    public static class TownMapBuilder
    {
        private const string SCENE_PATH = "Assets/Level/Scenes/Town/TownMap.unity";
        private const string ROOT_NAME = "TownMap";
        private const string ROAD_FOLDER = "Assets/Level/Prefabs/Stage/Road";

        // マップの半径から、道の外側の角を内側へ下げる余白（m）
        private const float EDGE_MARGIN = 2f;
        private const float MAP_RADIUS = 350f;
        private const float ROTARY_RADIUS = 40f;
        private const float STRAIGHT_LENGTH = 16f;
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

            var root = new GameObject(ROOT_NAME).transform;
            var roads = new GameObject("Roads").transform;
            roads.SetParent(root, false);
            var placed = BuildRoads(roads, TownLayout.CreateRoadLines());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"街のマップを組み立てました。道路の部品 {placed} 個。");
        }

        private static UnityEngine.SceneManagement.Scene OpenOrCreateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SCENE_PATH) != null)
                return EditorSceneManager.OpenScene(SCENE_PATH);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var path in _environmentPrefabs)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                if (path.Contains("AbyssFog")) instance.transform.position = new Vector3(0f, ABYSS_FOG_HEIGHT, 0f);
            }

            // 外壁とドームまで見えるよう、描画距離をマップの外まで延ばす
            var camera = Camera.main;
            camera.farClipPlane = 3000f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 2f, -120f), Quaternion.identity);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            return scene;
        }

        private static int BuildRoads(Transform parent, IReadOnlyList<RoadLine> lines)
        {
            var placed = 0;
            Place(parent, "Rotary", Vector3.zero, 0f, 1f);
            placed++;

            var crossings = new List<Crossing>();
            for (var i = 0; i < lines.Count; i++)
            {
                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (lines[i].AlongX == lines[j].AlongX) continue;
                    var wide = lines[i].Kind.HalfWidth >= lines[j].Kind.HalfWidth ? lines[i].Kind : lines[j].Kind;
                    var x = lines[i].AlongX ? lines[j].Offset : lines[i].Offset;
                    var z = lines[i].AlongX ? lines[i].Offset : lines[j].Offset;
                    // 中心の交点はロータリーが受け持つ
                    if (Mathf.Approximately(x, 0f) && Mathf.Approximately(z, 0f)) continue;
                    crossings.Add(new Crossing(new Vector2(x, z), wide));
                }
            }

            foreach (var crossing in crossings)
            {
                Place(parent, crossing.Kind.Name + "_Cross", new Vector3(crossing.Center.x, 0f, crossing.Center.y), 0f, 1f);
                placed++;
            }

            foreach (var line in lines)
            {
                foreach (var (start, end) in OpenSpans(line, crossings))
                    placed += FillStraight(parent, line, start, end);
            }

            return placed;
        }

        /// <summary>
        /// 線の上で、交点・ロータリー・崖にふさがれていない区間（線に沿った座標の範囲）。
        /// </summary>
        private static List<(float Start, float End)> OpenSpans(RoadLine line, List<Crossing> crossings)
        {
            var outer = line.Kind.HalfWidth + Mathf.Abs(line.Offset);
            var reach = Mathf.Sqrt((MAP_RADIUS - EDGE_MARGIN) * (MAP_RADIUS - EDGE_MARGIN) - outer * outer);

            var blocked = new List<(float Start, float End)>();
            if (Mathf.Approximately(line.Offset, 0f)) blocked.Add((-ROTARY_RADIUS, ROTARY_RADIUS));
            foreach (var crossing in crossings)
            {
                var across = line.AlongX ? crossing.Center.y : crossing.Center.x;
                if (!Mathf.Approximately(across, line.Offset)) continue;
                var along = line.AlongX ? crossing.Center.x : crossing.Center.y;
                blocked.Add((along - crossing.Kind.HalfWidth, along + crossing.Kind.HalfWidth));
            }

            blocked.Sort((a, b) => a.Start.CompareTo(b.Start));
            var spans = new List<(float, float)>();
            var cursor = -reach;
            foreach (var (start, end) in blocked)
            {
                if (start > cursor) spans.Add((cursor, Mathf.Min(start, reach)));
                cursor = Mathf.Max(cursor, end);
            }

            if (cursor < reach) spans.Add((cursor, reach));
            return spans;
        }

        private static int FillStraight(Transform parent, RoadLine line, float start, float end)
        {
            var length = end - start;
            var count = Mathf.Max(1, Mathf.RoundToInt(length / STRAIGHT_LENGTH));
            var tile = length / count;
            for (var i = 0; i < count; i++)
            {
                var along = start + tile * (i + 0.5f);
                var position = line.AlongX ? new Vector3(along, 0f, line.Offset) : new Vector3(line.Offset, 0f, along);
                Place(parent, line.Kind.Name + "_Straight", position, line.AlongX ? 90f : 0f, tile / STRAIGHT_LENGTH);
            }

            return count;
        }

        /// <summary>
        /// 道路の部品を置く。lengthScale は部品の Z（道の向き）の倍率。
        /// </summary>
        private static void Place(Transform parent, string part, Vector3 position, float yaw, float lengthScale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ROAD_FOLDER}/Stage_Road_{part}_01.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            instance.transform.localScale = new Vector3(1f, 1f, lengthScale);
        }

        private readonly struct Crossing
        {
            public readonly Vector2 Center;
            public readonly RoadKind Kind;

            public Crossing(Vector2 center, RoadKind kind)
            {
                Center = center;
                Kind = kind;
            }
        }
    }
}
