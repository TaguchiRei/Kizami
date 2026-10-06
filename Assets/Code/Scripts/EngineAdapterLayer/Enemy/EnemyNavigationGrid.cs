using Unity.Collections;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の経路の格子。縦の列ごとに「立てる層」（敵が立てる床の高さ）を下から順に持ち、層 1 つを 1 つのノードとする。
    /// Burst の Job へ渡して読むための値で、配列の持ち主は EnemyDistanceField。
    /// </summary>
    /// <remarks>
    /// 敵が高さ h から隣の列へ進むと、その列のうち「h ＋ 登れる高さ」以下で最も高い層に乗る。登るのは登れる高さまでで、降りるのは高さに制限がない。
    /// 列番号は z × 幅 ＋ x、ノード番号は 列番号 × MAX_LAYERS ＋ 層の番号。
    /// </remarks>
    public struct EnemyNavigationGrid
    {
        /// <summary> 1 つの列に持てる立てる層の数 </summary>
        public const int MAX_LAYERS = 4;

        /// <summary> ノードごとの床の高さ </summary>
        [ReadOnly] public NativeArray<float> Heights;

        /// <summary> 列ごとの立てる層の数 </summary>
        [ReadOnly] public NativeArray<byte> LayerCounts;

        /// <summary> 格子の範囲の最小の角（ワールド座標） </summary>
        public float3 Origin;

        /// <summary> マスの一辺（m） </summary>
        public float CellSize;

        /// <summary> 登れる段差の高さ（m） </summary>
        public float ClimbHeight;

        /// <summary> x 方向の列の数 </summary>
        public int Width;

        /// <summary> z 方向の列の数 </summary>
        public int Depth;

        /// <summary>
        /// 位置の真下の列を返す。格子の範囲の外なら false。
        /// </summary>
        public bool TryGetColumn(float3 position, out int column)
        {
            var x = (int)math.floor((position.x - Origin.x) / CellSize);
            var z = (int)math.floor((position.z - Origin.z) / CellSize);
            column = z * Width + x;
            return x >= 0 && x < Width && z >= 0 && z < Depth;
        }

        /// <summary>
        /// 列の中心の位置を返す。
        /// </summary>
        public float3 GetCellCenter(int column, float height)
        {
            return new float3(Origin.x + (column % Width + 0.5f) * CellSize, height, Origin.z + (column / Width + 0.5f) * CellSize);
        }

        /// <summary>
        /// 列のうち、高さ maxHeight 以下で最も高い層。なければ -1。
        /// </summary>
        public int GetHighestNodeBelow(int column, float maxHeight)
        {
            var node = -1;
            for (var k = 0; k < LayerCounts[column]; k++)
            {
                if (Heights[column * MAX_LAYERS + k] > maxHeight) break;

                node = column * MAX_LAYERS + k;
            }

            return node;
        }

        /// <summary>
        /// 高さ fromHeight から列 column へ進んだときに乗る層（fromHeight ＋ 登れる高さ以下で最も高い層）。なければ -1。
        /// </summary>
        public int GetLandingNode(int column, float fromHeight)
        {
            return GetHighestNodeBelow(column, fromHeight + ClimbHeight);
        }

        /// <summary>
        /// 高さ fromHeight から列 column へ進んだときに乗る層が、登れる高さの範囲で上下するだけか。
        /// 斜めに進むときに、間の 2 つの列について確かめる（壁の角を抜けない為）。
        /// </summary>
        public bool CanCross(int column, float fromHeight)
        {
            var node = GetLandingNode(column, fromHeight);
            return node >= 0 && math.abs(Heights[node] - fromHeight) <= ClimbHeight;
        }
    }
}
