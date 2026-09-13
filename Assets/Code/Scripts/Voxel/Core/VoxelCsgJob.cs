using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.Voxel
{
    /// <summary>
    /// 形状をボリュームへ合成する方法。
    /// </summary>
    public enum VoxelCsgOperation
    {
        /// <summary> 形状の内側を埋める（和集合） </summary>
        Union,

        /// <summary> 形状の内側を削る（差集合） </summary>
        Subtract
    }

    /// <summary>
    /// サンプル範囲内の各サンプルへ、形状の距離を CSG 合成する。
    ///
    /// 具象の形状ごとに [assembly: RegisterGenericJobType(typeof(VoxelCsgJob&lt;形状&gt;))] で登録すること。
    /// 登録が無いと Burst でコンパイルされない。
    /// </summary>
    [BurstCompile]
    public struct VoxelCsgJob<TShape> : IJobParallelFor where TShape : struct, IVoxelShape
    {
        /// <summary> 最外周のサンプルに強制する最小の距離（ボクセル数換算） </summary>
        private const float BoundaryMinVoxels = 0.01f;

        [NativeDisableParallelForRestriction] public NativeArray<float> Samples;
        public VoxelGridLayout Layout;
        public TShape Shape;
        public VoxelCsgOperation Operation;
        public int3 RangeMin;
        public int3 RangeSize;
        public float TruncationDistance;

        public void Execute(int index)
        {
            var offset = new int3(
                index % RangeSize.x,
                index / RangeSize.x % RangeSize.y,
                index / (RangeSize.x * RangeSize.y));
            var sample = RangeMin + offset;
            var sampleIndex = Layout.ToSampleIndex(sample);

            var shapeDistance = Shape.Distance(Layout.ToLocalPosition(sample));
            var current = Samples[sampleIndex];
            var result = Operation == VoxelCsgOperation.Union
                ? math.min(current, shapeDistance)
                : math.max(current, -shapeDistance);
            result = math.clamp(result, -TruncationDistance, TruncationDistance);

            // 最外周を外側に保つ。メッシュが格子の端で開かない為の不変条件
            if (Layout.IsBoundarySample(sample))
            {
                result = math.max(result, Layout.VoxelSize * BoundaryMinVoxels);
            }

            Samples[sampleIndex] = result;
        }
    }
}
