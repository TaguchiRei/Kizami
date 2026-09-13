using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.Voxel
{
    /// <summary>
    /// 1 チャンク分の等値面（距離 0 の面）を Surface Nets で抽出する。
    ///
    /// 表面をまたぐセルごとに頂点を 1 つ置き（セルの辺と表面の交点の平均）、
    /// 表面をまたぐ格子の辺ごとに、その辺を囲む 4 セルの頂点を四角形で結ぶ。
    /// 法線は各格子点の中心差分の勾配を、頂点位置でトリリニア補間して求める。
    /// 頂点と法線はボクセル空間（ローカル空間）で出力する。
    ///
    /// 読むサンプル範囲は VoxelGridLayout.MeshingReadMargin と対応している。
    /// </summary>
    [BurstCompile]
    public struct SurfaceNetsJob : IJob
    {
        [ReadOnly] public NativeArray<float> Samples;
        public VoxelGridLayout Layout;
        public int3 Chunk;

        public NativeList<float3> Vertices;
        public NativeList<float3> Normals;
        public NativeList<int> Indices;

        public void Execute()
        {
            Layout.GetChunkVertexCellRange(Chunk, out var vertexCellMin, out var vertexCellMax);
            var vertexCellSize = vertexCellMax - vertexCellMin;
            var cellToVertex = new NativeArray<int>(vertexCellSize.x * vertexCellSize.y * vertexCellSize.z,
                Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            GenerateVertices(vertexCellMin, vertexCellSize, cellToVertex);
            GenerateQuads(vertexCellMin, vertexCellSize, cellToVertex);

            cellToVertex.Dispose();
        }

        /// <summary>
        /// 表面をまたぐセルに頂点を置き、セルから頂点番号を引く表を埋める。表面をまたがないセルは -1。
        /// </summary>
        private void GenerateVertices(int3 vertexCellMin, int3 vertexCellSize, NativeArray<int> cellToVertex)
        {
            for (var z = 0; z < vertexCellSize.z; z++)
            for (var y = 0; y < vertexCellSize.y; y++)
            for (var x = 0; x < vertexCellSize.x; x++)
            {
                var offset = new int3(x, y, z);
                var cell = vertexCellMin + offset;
                var slot = ToVertexSlot(offset, vertexCellSize);

                ReadCorners(cell, out var lower, out var upper);
                var insideMask = math.bitmask(lower < 0f) | (math.bitmask(upper < 0f) << 4);
                if (insideMask == 0 || insideMask == 0xFF)
                {
                    cellToVertex[slot] = -1;
                    continue;
                }

                var inCell = AverageCrossing(lower, upper);
                cellToVertex[slot] = Vertices.Length;
                Vertices.Add(Layout.Origin + ((float3)cell + inCell) * Layout.VoxelSize);
                Normals.Add(ComputeNormal(cell, inCell));
            }
        }

        /// <summary>
        /// チャンクのセル範囲を基点とする辺のうち、表面をまたぐものごとに四角形を張る。
        /// 表側は距離が増える向き（外側）に向ける。
        /// </summary>
        private void GenerateQuads(int3 vertexCellMin, int3 vertexCellSize, NativeArray<int> cellToVertex)
        {
            Layout.GetChunkCellRange(Chunk, out var cellMin, out var cellMax);

            for (var z = cellMin.z; z < cellMax.z; z++)
            for (var y = cellMin.y; y < cellMax.y; y++)
            for (var x = cellMin.x; x < cellMax.x; x++)
            {
                var point = new int3(x, y, z);
                var isInside = SampleAt(point) < 0f;

                for (var axis = 0; axis < 3; axis++)
                {
                    var axis1 = (axis + 1) % 3;
                    var axis2 = (axis + 2) % 3;

                    // 格子の最外周の面上の辺。最外周は常に外側なので表面をまたがない
                    if (point[axis1] == 0 || point[axis2] == 0) continue;

                    if (isInside == SampleAt(point + Unit(axis)) < 0f) continue;

                    var step1 = Unit(axis1);
                    var step2 = Unit(axis2);
                    var v00 = cellToVertex[ToVertexSlot(point - step1 - step2 - vertexCellMin, vertexCellSize)];
                    var v10 = cellToVertex[ToVertexSlot(point - step2 - vertexCellMin, vertexCellSize)];
                    var v11 = cellToVertex[ToVertexSlot(point - vertexCellMin, vertexCellSize)];
                    var v01 = cellToVertex[ToVertexSlot(point - step1 - vertexCellMin, vertexCellSize)];

                    // (axis, axis1, axis2) は巡回順なので cross(axis1, axis2) = +axis。
                    // v00→v10→v11 の外積は +axis を向き、Unity は外積が向く側を表面とする
                    if (isInside)
                    {
                        AddQuad(v00, v10, v11, v01);
                    }
                    else
                    {
                        AddQuad(v00, v01, v11, v10);
                    }
                }
            }
        }

        private void AddQuad(int a, int b, int c, int d)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
            Indices.Add(a);
            Indices.Add(c);
            Indices.Add(d);
        }

        /// <summary>
        /// セルの 8 隅の距離を読む。隅 i の位置は CornerOffset(i)。
        /// lower が z = 0 側の 4 隅、upper が z = 1 側の 4 隅。
        /// </summary>
        private void ReadCorners(int3 cell, out float4 lower, out float4 upper)
        {
            lower = new float4(
                SampleAt(cell),
                SampleAt(cell + new int3(1, 0, 0)),
                SampleAt(cell + new int3(0, 1, 0)),
                SampleAt(cell + new int3(1, 1, 0)));
            upper = new float4(
                SampleAt(cell + new int3(0, 0, 1)),
                SampleAt(cell + new int3(1, 0, 1)),
                SampleAt(cell + new int3(0, 1, 1)),
                SampleAt(cell + new int3(1, 1, 1)));
        }

        /// <summary>
        /// セルの 12 辺のうち表面をまたぐものについて、表面との交点のセル内座標（0〜1）を平均する。
        /// </summary>
        private static float3 AverageCrossing(float4 lower, float4 upper)
        {
            var sum = float3.zero;
            var count = 0;

            for (var a = 0; a < 8; a++)
            for (var bit = 1; bit < 8; bit <<= 1)
            {
                if ((a & bit) != 0) continue;

                var b = a | bit;
                var distanceA = Corner(lower, upper, a);
                var distanceB = Corner(lower, upper, b);
                if (distanceA < 0f == distanceB < 0f) continue;

                var t = distanceA / (distanceA - distanceB);
                sum += math.lerp(CornerOffset(a), CornerOffset(b), t);
                count++;
            }

            return sum / count;
        }

        /// <summary>
        /// セルの 8 隅の勾配を、セル内座標 inCell でトリリニア補間して正規化する。
        /// </summary>
        private float3 ComputeNormal(int3 cell, float3 inCell)
        {
            var gradient = float3.zero;

            for (var i = 0; i < 8; i++)
            {
                var offset = CornerOffset(i);
                var weights = math.select(1f - inCell, inCell, offset > 0.5f);
                gradient += weights.x * weights.y * weights.z * SampleGradient(cell + (int3)offset);
            }

            var length = math.length(gradient);
            return length > 1e-6f ? gradient / length : new float3(0f, 1f, 0f);
        }

        /// <summary>
        /// 格子点の距離の勾配を中心差分で求める。格子の端では片側差分になる。
        /// </summary>
        private float3 SampleGradient(int3 sample)
        {
            var lower = math.max(sample - 1, 0);
            var upper = math.min(sample + 1, Layout.SampleCount - 1);

            return new float3(
                (SampleAt(new int3(upper.x, sample.y, sample.z)) - SampleAt(new int3(lower.x, sample.y, sample.z)))
                / (upper.x - lower.x),
                (SampleAt(new int3(sample.x, upper.y, sample.z)) - SampleAt(new int3(sample.x, lower.y, sample.z)))
                / (upper.y - lower.y),
                (SampleAt(new int3(sample.x, sample.y, upper.z)) - SampleAt(new int3(sample.x, sample.y, lower.z)))
                / (upper.z - lower.z));
        }

        private float SampleAt(int3 sample)
        {
            return Samples[Layout.ToSampleIndex(sample)];
        }

        private static float Corner(float4 lower, float4 upper, int index)
        {
            return index < 4 ? lower[index] : upper[index - 4];
        }

        private static float3 CornerOffset(int index)
        {
            return new float3(index & 1, (index >> 1) & 1, index >> 2);
        }

        private static int3 Unit(int axis)
        {
            var unit = int3.zero;
            unit[axis] = 1;
            return unit;
        }

        private static int ToVertexSlot(int3 offset, int3 size)
        {
            return offset.x + size.x * (offset.y + size.y * offset.z);
        }
    }
}
