using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// モデル 1 つ分の、事前ベイクした SDF パーツの集まり。
    /// 距離データが大きい為、テキストではなくバイナリで保存する。
    /// </summary>
    /// <seealso href="https://github.com/TaguchiRei/Kizami/blob/main/Assets/Docs/Voxel/VoxelOverview.md">説明ドキュメント: Voxel</seealso>
    [Icon("Assets/Art/Textures/BoxelIcon.png")]
    [PreferBinarySerialization]
    public sealed class VoxelModelAsset : ScriptableObject
    {
        [SerializeField] private VoxelSdfData[] _parts = Array.Empty<VoxelSdfData>();

        [SerializeField]
        [Tooltip("パーツの素材の番号が指す素材の一覧。素材を持たないモデルでは空")]
        private VoxelMaterialEntry[] _materials = Array.Empty<VoxelMaterialEntry>();

        public IReadOnlyList<VoxelSdfData> Parts => _parts;

        public IReadOnlyList<VoxelMaterialEntry> Materials => _materials;

        /// <summary>
        /// パーツと素材の一覧を差し替える。ベイク処理から呼ぶ。
        /// </summary>
        public void SetParts(VoxelSdfData[] parts, VoxelMaterialEntry[] materials)
        {
            _parts = parts;
            _materials = materials;
        }
    }
}
