using Kizami.EngineAdapter.Voxel;
using Unity.Jobs;
using Unity.Mathematics;

[assembly: RegisterGenericJobType(typeof(VoxelCsgJob<SphereShape>))]

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 球。
    /// </summary>
    public readonly struct SphereShape : ITransformableVoxelShape<SphereShape>
    {
        public readonly float3 Center;
        public readonly float Radius;

        public SphereShape(float3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public VoxelBounds Bounds => VoxelBounds.FromCenterExtents(Center, Radius);

        public float Distance(float3 position)
        {
            return math.length(position - Center) - Radius;
        }

        public SphereShape Transformed(float4x4 matrix)
        {
            return new SphereShape(math.transform(matrix, Center), Radius * VoxelShapeTransform.UniformScale(matrix));
        }
    }
}
