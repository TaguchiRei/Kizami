namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 素材の強度の段階。値ごとの融点は VoxelThermalSettings で決める。
    /// </summary>
    public enum VoxelStrength : byte
    {
        Soft,
        Normal,
        Hard
    }
}
