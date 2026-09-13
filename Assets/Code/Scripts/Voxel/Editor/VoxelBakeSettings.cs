using System;
using UnityEngine;

namespace Kizami.Voxel.EditorTools
{
    /// <summary>
    /// モデルを SDF にベイクするときの設定。
    /// </summary>
    [Serializable]
    public struct VoxelBakeSettings
    {
        [Min(0.001f)]
        [Tooltip("ベイクするボクセルの一辺の長さ（モデルのローカル空間）。実行時の品質設定より細かくする。")]
        public float VoxelSize;

        [Min(1)]
        [Tooltip("メッシュの境界の外側に取る余白（ボクセル数）")]
        public int PaddingVoxels;

        [Min(0.001f)]
        [Tooltip("保持する距離の上限。実行時のボクセルの大きさの 2 倍以上にすること。")]
        public float MaxDistance;

        [Tooltip("true なら階層内の全メッシュを 1 パーツにまとめる。false ならメッシュごとに別パーツとしてベイクする。")]
        public bool CombineHierarchy;

        [Range(0, 19)]
        [Tooltip("内外判定を周囲のボクセルで補正する回数（MeshToSDFBaker の signPassesCount）")]
        public int SignPassCount;

        [Range(0f, 1f)]
        [Tooltip("内側と判定する閾値（MeshToSDFBaker の threshold）")]
        public float InOutThreshold;

        public static VoxelBakeSettings Default => new()
        {
            VoxelSize = 0.02f,
            PaddingVoxels = 2,
            MaxDistance = 0.3f,
            CombineHierarchy = false,
            SignPassCount = 1,
            InOutThreshold = 0.5f
        };
    }
}
