using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// ボリュームへ合成できる形状。ボクセル空間（VoxelPiece のローカル空間）で表す。
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

    /// <summary>
    /// 別の空間へ移せる形状。VoxelPiece.ApplyEdit にワールド空間の形状を渡す為に使う。
    /// </summary>
    /// <typeparam name="TShape">実装する形状自身の型</typeparam>
    public interface ITransformableVoxelShape<TShape> : IVoxelShape where TShape : struct
    {
        /// <summary>
        /// 行列で移した形状を返す。行列の拡大率は均一である前提。
        /// </summary>
        TShape Transformed(float4x4 matrix);
    }

    /// <summary>
    /// 形状を行列で移すときの計算。行列の拡大率は均一である前提。
    /// </summary>
    public static class VoxelShapeTransform
    {
        public static float UniformScale(float4x4 matrix)
        {
            return math.length(matrix.c0.xyz);
        }

        public static quaternion Rotation(float4x4 matrix)
        {
            var axes = new float3x3(
                math.normalize(matrix.c0.xyz),
                math.normalize(matrix.c1.xyz),
                math.normalize(matrix.c2.xyz));
            return new quaternion(axes);
        }
    }
}
