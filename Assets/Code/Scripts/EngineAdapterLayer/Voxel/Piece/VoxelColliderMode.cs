namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// VoxelObject の当たり判定の作り方。
    /// </summary>
    public enum VoxelColliderMode
    {
        /// <summary> 当たり判定を作らない </summary>
        None,

        /// <summary> チャンクごとに、メッシュそのままの MeshCollider を作る。動く Rigidbody には使えない </summary>
        ChunkMesh,

        /// <summary> 表面を包む凸包の MeshCollider を 1 つ作る。動く Rigidbody に使える </summary>
        ConvexHull
    }
}
