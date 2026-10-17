using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 5×7 のドット絵の文字を、1 ドットを 1 つの箱にした、ボクセル風の閉じたメッシュにする。
    /// 箱は隣り合う面を省いて 1 つのメッシュにまとめる。メッシュ切断（CuttableObject）で切れるよう、頂点・法線・UV を持つ読み書きできるメッシュにする。
    /// 原点は文字の中心。文字ごと・大きさごとに作ったメッシュは使い回す。
    /// </summary>
    public static class VoxelLetterMesh
    {
        /// <summary> 文字の横のドット数 </summary>
        public const int GLYPH_WIDTH = 5;

        /// <summary> 文字の縦のドット数 </summary>
        public const int GLYPH_HEIGHT = 7;

        /// <summary> 'A' から 'Z' の順。各文字は上の行から 7 行で、1 が箱のあるドット </summary>
        private static readonly string[][] _glyphs =
        {
            new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
            new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
            new[] { "01110", "10001", "10000", "10000", "10000", "10001", "01110" },
            new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
            new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
            new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
            new[] { "01110", "10001", "10000", "10111", "10001", "10001", "01111" },
            new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
            new[] { "01110", "00100", "00100", "00100", "00100", "00100", "01110" },
            new[] { "00111", "00010", "00010", "00010", "00010", "10010", "01100" },
            new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
            new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
            new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" },
            new[] { "10001", "10001", "11001", "10101", "10011", "10001", "10001" },
            new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
            new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
            new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" },
            new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
            new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
            new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
            new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
            new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
            new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" },
            new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
            new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" },
            new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" }
        };

        private static readonly Dictionary<(char Character, float CellSize, float Depth), Mesh> _cache = new();

        /// <summary>
        /// 文字のメッシュを返す。英字（大文字・小文字）だけ作れる。
        /// </summary>
        /// <param name="character">文字</param>
        /// <param name="cellSize">1 ドットの大きさ（m）</param>
        /// <param name="depth">奥行き（m）</param>
        /// <param name="mesh">作った（または使い回す）メッシュ</param>
        /// <returns>文字がドット絵になく、メッシュを作れなければ false</returns>
        public static bool TryGetMesh(char character, float cellSize, float depth, out Mesh mesh)
        {
            mesh = null;
            var upper = char.ToUpperInvariant(character);
            if (upper < 'A' || upper > 'Z') return false;

            var key = (upper, cellSize, depth);
            if (_cache.TryGetValue(key, out mesh) && mesh != null) return true;

            mesh = Build(_glyphs[upper - 'A'], cellSize, depth);
            mesh.name = $"VoxelLetter_{upper}";
            _cache[key] = mesh;
            return true;
        }

        /// <summary>
        /// ドットのある所に箱を並べ、隣り合う面を省いたメッシュを作る。
        /// </summary>
        private static Mesh Build(string[] rows, float cellSize, float depth)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            var width = GLYPH_WIDTH * cellSize;
            var height = GLYPH_HEIGHT * cellSize;
            var half = depth * 0.5f;

            for (var row = 0; row < GLYPH_HEIGHT; row++)
            {
                for (var column = 0; column < GLYPH_WIDTH; column++)
                {
                    if (!IsLit(rows, row, column)) continue;

                    var minX = (column - GLYPH_WIDTH * 0.5f) * cellSize;
                    var maxX = minX + cellSize;
                    var maxY = (GLYPH_HEIGHT * 0.5f - row) * cellSize;
                    var minY = maxY - cellSize;

                    AddQuad(vertices, normals, uvs, triangles, width, height, Vector3.back,
                        new Vector3(minX, minY, -half), new Vector3(minX, maxY, -half),
                        new Vector3(maxX, maxY, -half), new Vector3(maxX, minY, -half));
                    AddQuad(vertices, normals, uvs, triangles, width, height, Vector3.forward,
                        new Vector3(minX, minY, half), new Vector3(minX, maxY, half),
                        new Vector3(maxX, maxY, half), new Vector3(maxX, minY, half));

                    if (!IsLit(rows, row, column - 1))
                    {
                        AddQuad(vertices, normals, uvs, triangles, width, height, Vector3.left,
                            new Vector3(minX, minY, -half), new Vector3(minX, maxY, -half),
                            new Vector3(minX, maxY, half), new Vector3(minX, minY, half));
                    }

                    if (!IsLit(rows, row, column + 1))
                    {
                        AddQuad(vertices, normals, uvs, triangles, width, height, Vector3.right,
                            new Vector3(maxX, minY, -half), new Vector3(maxX, maxY, -half),
                            new Vector3(maxX, maxY, half), new Vector3(maxX, minY, half));
                    }

                    if (!IsLit(rows, row - 1, column))
                    {
                        AddQuad(vertices, normals, uvs, triangles, width, height, Vector3.up,
                            new Vector3(minX, maxY, -half), new Vector3(maxX, maxY, -half),
                            new Vector3(maxX, maxY, half), new Vector3(minX, maxY, half));
                    }

                    if (!IsLit(rows, row + 1, column))
                    {
                        AddQuad(vertices, normals, uvs, triangles, width, height, Vector3.down,
                            new Vector3(minX, minY, -half), new Vector3(maxX, minY, -half),
                            new Vector3(maxX, minY, half), new Vector3(minX, minY, half));
                    }
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool IsLit(string[] rows, int row, int column)
        {
            return row >= 0 && row < GLYPH_HEIGHT && column >= 0 && column < GLYPH_WIDTH && rows[row][column] == '1';
        }

        /// <summary>
        /// 四角形を 1 つ足す。法線の向きから表になる巻き順を選ぶ（Unity では、表から見て時計回りが表）。
        /// </summary>
        private static void AddQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles, float width, float height, Vector3 normal,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var start = vertices.Count;
            foreach (var corner in new[] { a, b, c, d })
            {
                vertices.Add(corner);
                normals.Add(normal);
                uvs.Add(new Vector2(corner.x / width + 0.5f, corner.y / height + 0.5f));
            }

            var isFront = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f;
            if (isFront)
            {
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            else
            {
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
        }
    }
}
