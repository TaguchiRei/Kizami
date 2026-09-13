using Kizami.Voxel;
using Unity.Jobs;
using Unity.Mathematics;

[assembly: RegisterGenericJobType(typeof(VoxelCsgJob<SphereShape>))]

namespace Kizami.Voxel
{
    /// <summary>
    /// 球。
    /// </summary>
    public readonly struct SphereShape : IVoxelShape
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
    }
}
