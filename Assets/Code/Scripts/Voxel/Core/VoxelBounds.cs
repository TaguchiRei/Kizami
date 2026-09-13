using Unity.Mathematics;

namespace Kizami.Voxel
{
    /// <summary>
    /// ボクセル空間（VoxelObject のローカル空間）上の軸平行境界ボックス。
    /// </summary>
    public readonly struct VoxelBounds
    {
        public readonly float3 Min;
        public readonly float3 Max;

        public VoxelBounds(float3 min, float3 max)
        {
            Min = min;
            Max = max;
        }

        public float3 Center => (Min + Max) * 0.5f;
        public float3 Size => Max - Min;

        /// <summary>
        /// 中心と半径（各軸の半分の長さ）から境界を作る。
        /// </summary>
        public static VoxelBounds FromCenterExtents(float3 center, float3 extents)
        {
            return new VoxelBounds(center - extents, center + extents);
        }

        /// <summary>
        /// 全方向に指定量だけ広げた境界を返す。
        /// </summary>
        public VoxelBounds Expand(float amount)
        {
            return new VoxelBounds(Min - amount, Max + amount);
        }
    }
}
