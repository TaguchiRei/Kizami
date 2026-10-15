namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// VoxelPiece の当たり判定の作り方。
    /// </summary>
    public enum VoxelColliderMode
    {
        /// <summary> 当たり判定を作らない </summary>
        None,

        /// <summary> チャンクごとに、メッシュそのままの MeshCollider を作る。動く Rigidbody には使えない </summary>
        ChunkMesh,

        /// <summary> 表面を包む凸包の MeshCollider を1つ作る。動く Rigidbody に使える </summary>
        ConvexHull,

        /// <summary> チャンクごとに、中身の点をまとめた箱の BoxCollider を作る。角ばった描き方の形と一致し、動く Rigidbody にも使える </summary>
        Boxes
    }
}
