using Kizami.EngineAdapter.Voxel;
using Unity.Jobs;
using Unity.Mathematics;

[assembly: RegisterGenericJobType(typeof(VoxelCsgJob<CapsuleShape>))]

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 線分 PointA–PointB から Radius 以内の領域。
    /// </summary>
    public readonly struct CapsuleShape : ITransformableVoxelShape<CapsuleShape>
    {
        public readonly float3 PointA;
        public readonly float3 PointB;
        public readonly float Radius;

        public CapsuleShape(float3 pointA, float3 pointB, float radius)
        {
            PointA = pointA;
            PointB = pointB;
            Radius = radius;
        }

        public VoxelBounds Bounds =>
            new VoxelBounds(math.min(PointA, PointB) - Radius, math.max(PointA, PointB) + Radius);

        public float Distance(float3 position)
        {
            var toPosition = position - PointA;
            var segment = PointB - PointA;
            var lengthSq = math.lengthsq(segment);
            var t = lengthSq > 0f ? math.saturate(math.dot(toPosition, segment) / lengthSq) : 0f;
            return math.length(toPosition - segment * t) - Radius;
        }

        public CapsuleShape Transformed(float4x4 matrix)
        {
            return new CapsuleShape(
                math.transform(matrix, PointA),
                math.transform(matrix, PointB),
                Radius * VoxelShapeTransform.UniformScale(matrix));
        }
    }
}
