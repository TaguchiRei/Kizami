using Unity.Mathematics;

namespace Kizami.Voxel
{
    /// <summary>
    /// ボリュームへ合成できる形状。ボクセル空間（VoxelObject のローカル空間）で表す。
    /// 実装する struct ごとに VoxelCsgJob を RegisterGenericJobType で登録すること。
    /// </summary>
    public interface IVoxelShape
    {
        /// <summary> 形状を包む境界ボックス </summary>
        VoxelBounds Bounds { get; }

        /// <summary>
        /// 形状の表面までの符号付き距離。負が内側。
        /// </summary>
        float Distance(float3 position);
    }
}
