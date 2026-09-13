using Kizami.EngineAdapter.Voxel;
using Unity.Jobs;
using Unity.Mathematics;

[assembly: RegisterGenericJobType(typeof(VoxelCsgJob<BoxShape>))]

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 回転できる直方体。
    /// </summary>
    public readonly struct BoxShape : ITransformableVoxelShape<BoxShape>
    {
        public readonly float3 Center;
        public readonly float3 HalfExtents;
        public readonly quaternion Rotation;

        public BoxShape(float3 center, float3 halfExtents) : this(center, halfExtents, quaternion.identity)
        {
        }

        public BoxShape(float3 center, float3 halfExtents, quaternion rotation)
        {
            Center = center;
            HalfExtents = halfExtents;
            Rotation = rotation;
        }

        public VoxelBounds Bounds
        {
            get
            {
                // 回転後の各軸を、ワールド軸へ射影した長さの和が境界の半径になる
                var axes = new float3x3(Rotation);
                var extents = math.abs(axes.c0) * HalfExtents.x
                              + math.abs(axes.c1) * HalfExtents.y
                              + math.abs(axes.c2) * HalfExtents.z;
                return VoxelBounds.FromCenterExtents(Center, extents);
            }
        }

        public float Distance(float3 position)
        {
            var local = math.mul(math.conjugate(Rotation), position - Center);
            var q = math.abs(local) - HalfExtents;
            return math.length(math.max(q, 0f)) + math.min(math.cmax(q), 0f);
        }

        public BoxShape Transformed(float4x4 matrix)
        {
            return new BoxShape(
                math.transform(matrix, Center),
                HalfExtents * VoxelShapeTransform.UniformScale(matrix),
                math.mul(VoxelShapeTransform.Rotation(matrix), Rotation));
        }
    }
}
