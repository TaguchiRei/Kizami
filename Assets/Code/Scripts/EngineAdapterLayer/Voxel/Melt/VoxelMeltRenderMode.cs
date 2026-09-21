namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 融解した粒の表示方法。
    /// </summary>
    public enum VoxelMeltRenderMode
    {
        /// <summary> 粒を滑らかにつないだ液面のメッシュ </summary>
        Surface,

        /// <summary> 粒ごとの、体積の等しい球 </summary>
        Spheres
    }
}
