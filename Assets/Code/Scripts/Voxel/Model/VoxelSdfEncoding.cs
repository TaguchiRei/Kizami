using Unity.Mathematics;

namespace Kizami.Voxel
{
    /// <summary>
    /// 事前ベイクした距離を short へ量子化・復元する。±maxDistance を short の全範囲に対応させる。
    /// </summary>
    public static class VoxelSdfEncoding
    {
        /// <summary>
        /// 距離を量子化する。±maxDistance を超える値は丸める。
        /// </summary>
        public static short Quantize(float distance, float maxDistance)
        {
            return (short)math.round(math.clamp(distance / maxDistance, -1f, 1f) * short.MaxValue);
        }

        public static float Dequantize(short value, float maxDistance)
        {
            return value * (maxDistance / short.MaxValue);
        }
    }
}
