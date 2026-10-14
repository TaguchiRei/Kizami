using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 強さと減衰率の攻撃 1 回分。ピースのローカル空間で、融点 0 の素材まで届く範囲と減衰率を持ち、同じ攻撃を当て直せる。
    /// 範囲はカプセルで持ち、球は両端が同じ点のカプセルにする。
    /// </summary>
    public readonly struct VoxelStrikeRecord
    {
        public readonly float3 PointA;
        public readonly float3 PointB;
        public readonly float Radius;

        /// <summary> ローカル空間の 1 あたりに減る強さ </summary>
        public readonly float Falloff;

        public CapsuleShape Reach => new(PointA, PointB, Radius);

        private VoxelStrikeRecord(float3 pointA, float3 pointB, float radius, float falloff)
        {
            PointA = pointA;
            PointB = pointB;
            Radius = radius;
            Falloff = falloff;
        }

        /// <summary>
        /// ローカル空間の範囲と減衰率から記録を作る。範囲が球かカプセルでなければ作らない。
        /// </summary>
        public static bool TryCreate<TShape>(in TShape localReach, float localFalloff, out VoxelStrikeRecord record)
            where TShape : struct, IVoxelShape
        {
            switch (localReach)
            {
                case SphereShape sphere:
                    record = new VoxelStrikeRecord(sphere.Center, sphere.Center, sphere.Radius, localFalloff);
                    return true;

                case CapsuleShape capsule:
                    record = new VoxelStrikeRecord(capsule.PointA, capsule.PointB, capsule.Radius, localFalloff);
                    return true;

                default:
                    record = default;
                    return false;
            }
        }
    }
}
