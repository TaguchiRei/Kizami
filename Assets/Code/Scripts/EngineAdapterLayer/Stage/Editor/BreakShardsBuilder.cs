using System.Collections.Generic;
using Kizami.EngineAdapter;
using UnityEditor;
using UnityEngine;

namespace Kizami.Editor
{
    /// <summary>
    /// バリアと装甲のパネルが壊れたときに飛ばす、事前に砕いた破片のプレハブを作る。破片のメッシュは 1 つのアセットにまとめて書き出す。
    /// 破片は元のパネルと同じ原点・向きで置く前提で作る（GlassShardGroup が板の位置・向き・大きさに合わせて置く）。
    /// バリアは直径 1 の球の殻を、球面に置いた点のボロノイ分割で砕く。地面より下になる下半分は作らない。
    /// 装甲のパネルは 1 辺 1 の箱を、前面に置いた点のボロノイ分割で砕き、パネルの拡大に合わせて薄い板になる。
    /// </summary>
    public static class BreakShardsBuilder
    {
        private const string MESH_FOLDER = "Assets/Art/Models/Shards";
        private const string BARRIER_MESH_PATH = MESH_FOLDER + "/BarrierShards.asset";
        private const string ARMOR_MESH_PATH = MESH_FOLDER + "/ArmorShards.asset";
        private const string BARRIER_MATERIAL_PATH = "Assets/Art/Materials/BarrierShard.mat";
        private const string ARMOR_MATERIAL_PATH = "Assets/Art/Materials/ArmorShard.mat";
        private const string BARRIER_PREFAB_PATH = "Assets/Level/Prefabs/Enemy/BarrierShards.prefab";
        private const string ARMOR_PREFAB_PATH = "Assets/Level/Prefabs/Stage/ArmorShards.prefab";

        /// <summary> 球を割る細かさ。正二十面体を再分割する回数 </summary>
        private const int SPHERE_SUBDIVISIONS = 5;

        private const int SEED = 20261024;

        [MenuItem("Kizami/Stage/Build Break Shards")]
        public static void BuildAll()
        {
            System.IO.Directory.CreateDirectory(MESH_FOLDER);
            BuildBarrierShards(72, -8f, 0.94f);
            BuildArmorShards(4, 0.92f);
            AssetDatabase.SaveAssets();
            Debug.Log("バリアと装甲の破片のプレハブを作りました。");
        }

        /// <summary>
        /// 直径 1 の球の殻を、sites 個の点のボロノイ分割で砕いた破片のプレハブを作る。
        /// </summary>
        /// <param name="sites">破片の元になる点の数</param>
        /// <param name="minElevationDeg">破片の中心の仰角がこの角度（度）より低い破片は作らない</param>
        /// <param name="gapScale">破片を中心へ縮める倍率。1 未満で破片どうしに隙間ができる</param>
        private static void BuildBarrierShards(int sites, float minElevationDeg, float gapScale)
        {
            Sphere(SPHERE_SUBDIVISIONS, out var vertices, out var triangles);
            var random = new System.Random(SEED);
            var points = new Vector3[sites];
            var goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (var i = 0; i < sites; i++)
            {
                var y = 1f - 2f * (i + 0.5f) / sites;
                var radius = Mathf.Sqrt(1f - y * y);
                var angle = goldenAngle * i + (float)random.NextDouble() * 0.15f;
                points[i] = new Vector3(Mathf.Cos(angle) * radius, y + ((float)random.NextDouble() - 0.5f) * 0.03f, Mathf.Sin(angle) * radius).normalized;
            }

            var patches = new List<int>[sites];
            for (var i = 0; i < sites; i++) patches[i] = new List<int>();
            for (var t = 0; t < triangles.Count; t += 3)
            {
                var center = (vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]]).normalized;
                var best = 0;
                var bestDot = float.MinValue;
                for (var i = 0; i < sites; i++)
                {
                    var dot = Vector3.Dot(center, points[i]);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = i;
                    }
                }

                patches[best].Add(t);
            }

            var meshes = new List<Mesh>();
            var pivots = new List<Vector3>();
            for (var i = 0; i < sites; i++)
            {
                if (patches[i].Count == 0) continue;

                var pivot = Vector3.zero;
                foreach (var t in patches[i]) pivot += vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]];
                pivot /= patches[i].Count * 3;
                var elevation = Mathf.Asin(Mathf.Clamp(pivot.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (elevation < minElevationDeg) continue;

                var map = new Dictionary<int, int>();
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var indices = new List<int>();
                foreach (var t in patches[i])
                {
                    for (var k = 0; k < 3; k++)
                    {
                        var source = triangles[t + k];
                        if (!map.TryGetValue(source, out var local))
                        {
                            local = positions.Count;
                            map.Add(source, local);
                            positions.Add((vertices[source] - pivot) * gapScale);
                            normals.Add(vertices[source].normalized);
                        }

                        indices.Add(local);
                    }
                }

                var mesh = new Mesh { name = $"BarrierShard_{meshes.Count:00}" };
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateBounds();
                meshes.Add(mesh);
                pivots.Add(pivot);
            }

            var material = GetOrCreateMaterial(BARRIER_MATERIAL_PATH, "Kizami/BarrierShard");
            SaveMeshes(BARRIER_MESH_PATH, meshes);
            SavePrefab(BARRIER_PREFAB_PATH, "BarrierShards", BARRIER_MESH_PATH, pivots, material);
            Debug.Log($"バリアの破片を {meshes.Count} 個作りました（点の数 {sites}）。");
        }

        /// <summary>
        /// 1 辺 1 の箱を、前面（+Z 側）に置いた gridSize × gridSize 個の揺らした点のボロノイ分割で砕いた破片のプレハブを作る。
        /// </summary>
        /// <param name="gridSize">点を置く格子の 1 辺の数</param>
        /// <param name="gapScale">破片を中心へ縮める倍率</param>
        private static void BuildArmorShards(int gridSize, float gapScale)
        {
            var random = new System.Random(SEED + 1);
            var sites = new List<Vector2>();
            for (var x = 0; x < gridSize; x++)
            {
                for (var y = 0; y < gridSize; y++)
                {
                    var jitter = new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 0.7f;
                    sites.Add(new Vector2((x + 0.5f + jitter.x) / gridSize - 0.5f, (y + 0.5f + jitter.y) / gridSize - 0.5f));
                }
            }

            var meshes = new List<Mesh>();
            var pivots = new List<Vector3>();
            for (var i = 0; i < sites.Count; i++)
            {
                var polygon = new List<Vector2>
                {
                    new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0.5f, 0.5f), new(-0.5f, 0.5f)
                };
                for (var j = 0; j < sites.Count && polygon.Count > 0; j++)
                {
                    if (j != i) polygon = ClipByBisector(polygon, sites[i], sites[j]);
                }

                if (polygon.Count < 3) continue;

                var centroid = Vector2.zero;
                foreach (var point in polygon) centroid += point;
                centroid /= polygon.Count;

                var mesh = ExtrudePolygon(polygon, centroid, gapScale);
                mesh.name = $"ArmorShard_{meshes.Count:00}";
                meshes.Add(mesh);
                pivots.Add(new Vector3(centroid.x, centroid.y, 0f));
            }

            var material = GetOrCreateMaterial(ARMOR_MATERIAL_PATH, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", new Color(0.45f, 0.55f, 0.65f));
            EditorUtility.SetDirty(material);
            SaveMeshes(ARMOR_MESH_PATH, meshes);
            SavePrefab(ARMOR_PREFAB_PATH, "ArmorShards", ARMOR_MESH_PATH, pivots, material);
            Debug.Log($"装甲の破片を {meshes.Count} 個作りました。");
        }

        /// <summary>
        /// 凸多角形を、点 self の側（点 other より self に近い側）の半平面で切る。
        /// </summary>
        private static List<Vector2> ClipByBisector(List<Vector2> polygon, Vector2 self, Vector2 other)
        {
            var normal = self - other;
            var midpoint = (self + other) * 0.5f;
            var result = new List<Vector2>();
            for (var i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % polygon.Count];
                var currentSide = Vector2.Dot(current - midpoint, normal);
                var nextSide = Vector2.Dot(next - midpoint, normal);
                if (currentSide >= 0f) result.Add(current);
                if ((currentSide >= 0f) != (nextSide >= 0f))
                {
                    result.Add(current + (next - current) * (currentSide / (currentSide - nextSide)));
                }
            }

            return result;
        }

        /// <summary>
        /// 凸多角形を、Z が -0.5 から 0.5 の厚みに押し出したメッシュにする。頂点は pivot からの相対位置にし、gapScale で縮める。
        /// </summary>
        private static Mesh ExtrudePolygon(List<Vector2> polygon, Vector2 pivot, float gapScale)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var indices = new List<int>();

            for (var face = 0; face < 2; face++)
            {
                var z = face == 0 ? 0.5f : -0.5f;
                var normal = face == 0 ? Vector3.forward : Vector3.back;
                var start = positions.Count;
                foreach (var point in polygon)
                {
                    var local = (point - pivot) * gapScale;
                    positions.Add(new Vector3(local.x, local.y, z));
                    normals.Add(normal);
                }

                for (var k = 1; k < polygon.Count - 1; k++)
                {
                    // Unity は時計回りが表で、面の表の向きは cross(b - a, c - a)。前面（+Z）は多角形の向きのまま、背面（-Z）は逆順にする
                    if (face == 0)
                    {
                        indices.Add(start);
                        indices.Add(start + k);
                        indices.Add(start + k + 1);
                    }
                    else
                    {
                        indices.Add(start);
                        indices.Add(start + k + 1);
                        indices.Add(start + k);
                    }
                }
            }

            for (var i = 0; i < polygon.Count; i++)
            {
                var a = (polygon[i] - pivot) * gapScale;
                var b = (polygon[(i + 1) % polygon.Count] - pivot) * gapScale;
                var edge = new Vector3(b.x - a.x, b.y - a.y, 0f);
                var normal = Vector3.Cross(edge, Vector3.forward).normalized;
                var start = positions.Count;
                positions.Add(new Vector3(a.x, a.y, 0.5f));
                positions.Add(new Vector3(b.x, b.y, 0.5f));
                positions.Add(new Vector3(b.x, b.y, -0.5f));
                positions.Add(new Vector3(a.x, a.y, -0.5f));
                for (var k = 0; k < 4; k++) normals.Add(normal);
                indices.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            var mesh = new Mesh();
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 直径 1 の球を、正二十面体を subdivisions 回再分割して作る。
        /// </summary>
        private static void Sphere(int subdivisions, out List<Vector3> vertices, out List<int> triangles)
        {
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            vertices = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
            };
            for (var i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].normalized * 0.5f;

            triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };

            for (var level = 0; level < subdivisions; level++)
            {
                var cache = new Dictionary<long, int>();
                var next = new List<int>(triangles.Count * 4);
                for (var i = 0; i < triangles.Count; i += 3)
                {
                    var a = triangles[i];
                    var b = triangles[i + 1];
                    var c = triangles[i + 2];
                    var ab = Midpoint(a, b, vertices, cache);
                    var bc = Midpoint(b, c, vertices, cache);
                    var ca = Midpoint(c, a, vertices, cache);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }

                triangles = next;
            }
        }

        private static int Midpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
        {
            var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out var index)) return index;

            vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized * 0.5f);
            index = vertices.Count - 1;
            cache.Add(key, index);
            return index;
        }

        private static Material GetOrCreateMaterial(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// メッシュを 1 つのアセットにまとめて書き出す。同じパスのアセットがあれば作り直す。
        /// </summary>
        private static void SaveMeshes(string path, List<Mesh> meshes)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(meshes[0], path);
            for (var i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], path);
        }

        /// <summary>
        /// 破片を子に並べたプレハブを作る。子 i はアセットの i 番目のメッシュを持ち、pivots[i] に置く。
        /// </summary>
        private static void SavePrefab(string prefabPath, string rootName, string meshPath, List<Vector3> pivots, Material material)
        {
            var meshes = AssetDatabase.LoadAllAssetsAtPath(meshPath);
            var sorted = new List<Mesh>();
            foreach (var asset in meshes)
            {
                if (asset is Mesh mesh) sorted.Add(mesh);
            }

            sorted.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            var root = new GameObject(rootName);
            try
            {
                for (var i = 0; i < sorted.Count; i++)
                {
                    var shard = new GameObject($"Shard_{i:00}");
                    shard.transform.SetParent(root.transform, false);
                    shard.transform.localPosition = pivots[i];
                    shard.AddComponent<MeshFilter>().sharedMesh = sorted[i];
                    var renderer = shard.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
