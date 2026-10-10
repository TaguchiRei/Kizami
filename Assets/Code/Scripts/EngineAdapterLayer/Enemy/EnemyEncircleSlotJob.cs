using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// プレイヤーを囲む螺旋の上の置き場（部隊の目標位置）の位置を求める Job。
    /// Application（EnemySquadService）がそのフレームのプレイヤーの位置で置き場を割り当てられるよう、EnemySpawnAdapter が距離マップを更新したあと、観測を書く前に実行する。
    /// </summary>
    [BurstCompile]
    public struct EnemyEncircleSlotJob : IJob
    {
        /// <summary> 置き場が立てる層のない列に来たときに、ずらす先の列を探す半径（m） </summary>
        private const float SLOT_SHIFT_RADIUS = 10f;

        /// <summary> 置き場ごとの、立てる列へずらした位置。使えない置き場は NaN </summary>
        public NativeArray<float2> SlotPoints;

        /// <summary> 0 番に、使える置き場のうち最も外の置き場の番号を書く。なければ -1 </summary>
        public NativeArray<int> LastUsableSlot;

        public EnemyNavigationGrid Grid;
        [ReadOnly] public NativeArray<float> Distances;

        /// <summary> Distances を計算した追跡範囲の、最小の列 (x, z) </summary>
        public int2 TrackingMin;

        /// <summary> Distances を計算した追跡範囲の、最大の列 (x, z)。この列も含む </summary>
        public int2 TrackingMax;

        public EnemyFormationSettings Formation;

        /// <summary> プレイヤーの位置。置き場の螺旋の中心 </summary>
        public float3 PlayerPosition;

        /// <summary>
        /// 置き場ごとに、プレイヤーを中心にした螺旋の上の位置を、プレイヤーへたどり着ける立てる層のある列へずらして SlotPoints に書き、
        /// 使える置き場のうち最も外の置き場の番号を LastUsableSlot に書く（なければ -1）。
        /// それより内側で、周り SLOT_SHIFT_RADIUS に立てる列がない置き場（建物の中や穴の上）は NaN にする。
        /// それより外の置き場は、置き場を持てない部隊が待つ置き場にする。追跡範囲の外に出る位置は範囲の中へ寄せてから、立てる列へずらす
        /// （ずらせなければ寄せた位置のまま）。待つグループどうしも、置き場と同じ間隔をあける為。
        /// </summary>
        public void Execute()
        {
            var slotPoints = SlotPoints;
            var lastUsableSlot = -1;
            var searchRadius = (int)math.ceil(SLOT_SHIFT_RADIUS / Grid.CellSize);
            for (var slot = 0; slot < slotPoints.Length; slot++)
            {
                var point = GetSpiralPoint(slot);
                if (!TryShiftToReachableColumn(point, searchRadius, out var shifted))
                {
                    slotPoints[slot] = new float2(float.NaN);
                    continue;
                }

                slotPoints[slot] = shifted;
                lastUsableSlot = slot;
            }

            var areaMin = Grid.Origin.xz + ((float2)TrackingMin + 0.5f) * Grid.CellSize;
            var areaMax = Grid.Origin.xz + ((float2)TrackingMax + 0.5f) * Grid.CellSize;
            for (var slot = lastUsableSlot + 1; slot < slotPoints.Length; slot++)
            {
                var point = math.clamp(GetSpiralPoint(slot), areaMin, areaMax);
                slotPoints[slot] = TryShiftToReachableColumn(point, searchRadius, out var shifted) ? shifted : point;
            }

            LastUsableSlot[0] = lastUsableSlot;
        }

        /// <summary>
        /// プレイヤーを中心にした螺旋の上の、置き場の位置（水平）。
        /// </summary>
        private float2 GetSpiralPoint(int slot)
        {
            return PlayerPosition.xz + EnemyFormationSettings.GetSpiralOffset(slot, Formation.EncircleInnerRadius,
                Formation.EncircleLoopSpacing, Formation.EncircleSlotSpacing);
        }

        /// <summary>
        /// point の列から searchRadius 列までを近い順に調べ、プレイヤーへたどり着ける層のある最初の周の中で、point に最も近い列の中心を返す。
        /// point の列にあれば point をそのまま返す。
        /// </summary>
        private bool TryShiftToReachableColumn(float2 point, int searchRadius, out float2 shifted)
        {
            shifted = point;
            var origin = Grid.Origin.xz;
            var x = (int)math.floor((point.x - origin.x) / Grid.CellSize);
            var z = (int)math.floor((point.y - origin.y) / Grid.CellSize);

            for (var radius = 0; radius <= searchRadius; radius++)
            {
                var bestDistanceSq = float.MaxValue;
                for (var dz = -radius; dz <= radius; dz++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != radius) continue;

                        var nx = x + dx;
                        var nz = z + dz;
                        if (nx < TrackingMin.x || nx > TrackingMax.x || nz < TrackingMin.y || nz > TrackingMax.y) continue;

                        var column = nz * Grid.Width + nx;
                        if (!HasReachableNode(column)) continue;

                        var center = Grid.GetCellCenter(column, 0f).xz;
                        var distanceSq = math.distancesq(center, point);
                        if (distanceSq >= bestDistanceSq) continue;

                        bestDistanceSq = distanceSq;
                        shifted = radius == 0 ? point : center;
                    }
                }

                if (bestDistanceSq < float.MaxValue) return true;
            }

            return false;
        }

        private bool HasReachableNode(int column)
        {
            for (var k = 0; k < Grid.LayerCounts[column]; k++)
            {
                if (!float.IsPositiveInfinity(Distances[column * EnemyNavigationGrid.MAX_LAYERS + k])) return true;
            }

            return false;
        }
    }
}
