using Unity.Collections;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の経路の格子。縦の列ごとに「立てる層」（敵が立てる床の高さ）を下から順に持ち、層 1 つを 1 つのノードとする。
    /// Burst の Job へ渡して読むための値で、配列の持ち主は EnemyDistanceField。
    /// </summary>
    /// <remarks>
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

        /// <summary> 歩いて降りられる段差の高さ（m）。足場が壊れて落ちるのは、この高さによらない </summary>
        public float DropHeight;

        /// <summary> x 方向の列の数 </summary>
        public int Width;

        /// <summary> z 方向の列の数 </summary>
        public int Depth;

        /// <summary>
        /// 隣の 8 列への向きの番号から、列のずれを返す。番号は z、x の順に -1〜1 を並べ、ずれのない中央を飛ばしたもの。
        /// </summary>
        public static int2 GetNeighborOffset(int direction)
        {
            var cell = direction < 4 ? direction : direction + 1;
            return new int2(cell % 3 - 1, cell / 3 - 1);
        }

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
        /// 高さ fromHeight から列 column へ進んだときに乗る層（fromHeight ＋ 登れる高さ以下で最も高い層）。
        /// なければ、またはその層が降りられる高さより低ければ -1。
        /// </summary>
        public int GetLandingNode(int column, float fromHeight)
        {
            var node = GetHighestNodeBelow(column, fromHeight + ClimbHeight);
            return node >= 0 && Heights[node] >= fromHeight - DropHeight ? node : -1;
        }

        /// <summary>
        /// 列のうち、高さ height との差が登れる高さ以内の層で、最も高いもの。なければ -1。
        /// 隊列の位置を、道筋と同じ高さの床に置くときに使う。
        /// </summary>
        public int GetNodeNear(int column, float height)
        {
            var node = GetHighestNodeBelow(column, height + ClimbHeight);
            return node >= 0 && Heights[node] >= height - ClimbHeight ? node : -1;
        }

        /// <summary>
        /// 高さ height で列 column にいるとき、隣の 8 列のうち、進んで乗る層の距離が distance より小さく、最も小さい列を返す。
        /// 斜めに進むときは、間の 2 つの列を通れることも確かめる（壁の角を抜けない為）。
        /// </summary>
        /// <param name="distances">ノードごとのプレイヤーまでの距離</param>
        public bool TryGetDownhillColumn(int column, float height, float distance, NativeArray<float> distances,
            out int nextColumn)
        {
            nextColumn = -1;
            var bestDistance = distance;
            var x = column % Width;
            var z = column / Width;

            for (var direction = 0; direction < 8; direction++)
            {
                var offset = GetNeighborOffset(direction);
                var nx = x + offset.x;
                var nz = z + offset.y;
                if (nx < 0 || nx >= Width || nz < 0 || nz >= Depth) continue;

                var candidate = nz * Width + nx;
                var landing = GetLandingNode(candidate, height);
                if (landing < 0 || distances[landing] >= bestDistance) continue;

                if (offset.x != 0 && offset.y != 0
                    && !(CanCross(z * Width + nx, height) && CanCross(nz * Width + x, height))) continue;

                bestDistance = distances[landing];
                nextColumn = candidate;
            }

            return nextColumn >= 0;
        }

        /// <summary>
        /// 高さ height で列 column にいるとき、隣の 8 列のうち、進んで乗れて、中心が水平の位置 target に今の列より近く、最も近い列を返す。
        /// 距離マップはプレイヤーへの経路しか持たないので、プレイヤー以外の点（隊列の位置や包囲の置き場）へ回り込むときに使う。
        /// </summary>
        public bool TryGetColumnToward(int column, float height, float2 target, out int nextColumn)
        {
            nextColumn = -1;
            var bestDistanceSq = math.distancesq(GetCellCenter(column, 0f).xz, target);
            var x = column % Width;
            var z = column / Width;

            for (var direction = 0; direction < 8; direction++)
            {
                var offset = GetNeighborOffset(direction);
                var nx = x + offset.x;
                var nz = z + offset.y;
                if (nx < 0 || nx >= Width || nz < 0 || nz >= Depth) continue;

                var candidate = nz * Width + nx;
                var distanceSq = math.distancesq(GetCellCenter(candidate, 0f).xz, target);
                if (distanceSq >= bestDistanceSq || GetLandingNode(candidate, height) < 0) continue;

                if (offset.x != 0 && offset.y != 0
                    && !(CanCross(z * Width + nx, height) && CanCross(nz * Width + x, height))) continue;

                bestDistanceSq = distanceSq;
                nextColumn = candidate;
            }

            return nextColumn >= 0;
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

        /// <summary>
        /// 高さ height で列 column にいるとき、水平の位置 probe の列へ進めないか。同じ列なら進める。
        /// </summary>
        public bool IsBlocked(int column, float height, float2 probe)
        {
            if (!TryGetColumn(new float3(probe.x, height, probe.y), out var probeColumn)) return true;

            return probeColumn != column && GetLandingNode(probeColumn, height) < 0;
        }

        /// <summary>
        /// origin から水平の向き direction へマスの一辺ずつ進み、origin と同じ高さの床が続く長さ（m）を maxLength まで返す。
        /// </summary>
        public float GetFlatFloorLength(float3 origin, float2 direction, float maxLength)
        {
            var steps = (int)math.ceil(maxLength / CellSize);
            for (var s = 1; s <= steps; s++)
            {
                var length = math.min(s * CellSize, maxLength);
                var point = origin + new float3(direction.x, 0f, direction.y) * length;
                if (!TryGetColumn(point, out var column) || GetNodeNear(column, origin.y) < 0)
                {
                    return (s - 1) * CellSize;
                }
            }

            return maxLength;
        }
    }
}
