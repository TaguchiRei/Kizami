using Kizami.EngineAdapter.Voxel;
using Unity.Jobs;
using Unity.Mathematics;

[assembly: RegisterGenericJobType(typeof(VoxelCsgJob<BoxShape>))]
[assembly: RegisterGenericJobType(typeof(VoxelHeatJob<BoxShape>))]
[assembly: RegisterGenericJobType(typeof(VoxelParticleHeatJob<BoxShape>))]

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

        public BoxShape(float3 center, float3 halfExtents) : this(center, halfExtents, quaternion.identity)
        {
        }

        public BoxShape(float3 center, float3 halfExtents, quaternion rotation)
        {
            Center = center;
            HalfExtents = halfExtents;
            Rotation = rotation;
        }

        /// <summary>
        /// 平面の法線側を覆う箱を作る。箱の 1 面が平面に重なり、coverBounds の内側では、
        /// 箱までの距離が法線側の半空間までの距離と一致する。
        /// </summary>
        /// <param name="planePoint">平面上の点</param>
        /// <param name="planeNormal">平面の法線。正規化済みであること。この向きの側が箱の内側になる</param>
        /// <param name="coverBounds">距離が半空間と一致すべき範囲</param>
        public static BoxShape FromHalfSpace(float3 planePoint, float3 planeNormal, VoxelBounds coverBounds)
        {
            // 範囲内の点は、範囲の中心から coverRadius 以内にある。
            // 平面に沿う方向へ 2 倍、法線方向へは平面から範囲の最も遠い点の先まで広げ、
            // 範囲内の点の最も近い面が常に平面に重なる面になるようにする
            var center = coverBounds.Center;
            var coverRadius = math.length(coverBounds.Size) * 0.5f;
            var centerHeight = math.dot(center - planePoint, planeNormal);
            var projectedCenter = center - planeNormal * centerHeight;
            var halfDepth = (math.abs(centerHeight) + coverRadius * 2f) * 0.5f;

            var up = math.abs(planeNormal.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
            return new BoxShape(
                projectedCenter + planeNormal * halfDepth,
                new float3(coverRadius * 2f, coverRadius * 2f, halfDepth),
                quaternion.LookRotation(planeNormal, up));
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
