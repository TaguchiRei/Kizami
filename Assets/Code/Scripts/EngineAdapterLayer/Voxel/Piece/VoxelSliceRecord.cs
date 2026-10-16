using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 平面で切った1回分。ピースのローカル空間の平面を持ち、同じ切り方を当て直せる。
    /// </summary>
    public readonly struct VoxelSliceRecord
    {
        /// <summary> 平面上の点 </summary>
        public readonly float3 PlanePoint;

        /// <summary> 平面の法線。正規化済み </summary>
        public readonly float3 PlaneNormal;

        public VoxelSliceRecord(float3 planePoint, float3 planeNormal)
        {
            PlanePoint = planePoint;
            PlaneNormal = planeNormal;
        }
    }
}
