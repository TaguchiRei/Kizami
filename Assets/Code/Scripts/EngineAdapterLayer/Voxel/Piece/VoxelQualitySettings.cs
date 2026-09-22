using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// ボクセルの精度と、再メッシュ化の負荷に関わる設定。
    /// </summary>
    /// <seealso href="https://github.com/TaguchiRei/Kizami/blob/main/Assets/Docs/Voxel/VoxelOverview.md">説明ドキュメント: Voxel</seealso>
    [CreateAssetMenu(menuName = "Kizami/Voxel/Quality Settings", fileName = "VoxelQualitySettings")]
    public sealed class VoxelQualitySettings : ScriptableObject
    {
        [SerializeField, Min(0.001f)]
        [Tooltip("ボクセル 1 つの一辺の長さ（ワールド空間, m）")]
        private float _voxelSize = 0.03f;

        [SerializeField, Range(8, 64)]
        [Tooltip("チャンク 1 つの一辺のセル数。メッシュとコライダーはこの単位で作り直す。")]
        private int _chunkSize = 32;

        [SerializeField, Min(1)]
        [Tooltip("1 フレームで再メッシュ化するチャンクの上限数")]
        private int _remeshChunksPerFrame = 8;

        public float VoxelSize => _voxelSize;
        public int ChunkSize => _chunkSize;
        public int RemeshChunksPerFrame => _remeshChunksPerFrame;
    }
}
