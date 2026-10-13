using System;
using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// ボクセルの素材 1 つ。点ごとの素材の番号は、モデルの素材の一覧でのこの要素の番号を指す。
    /// </summary>
    [Serializable]
    public struct VoxelMaterialEntry
    {
        /// <summary> この素材の面を描くマテリアル </summary>
        public Material Material;

        /// <summary> 強度の段階 </summary>
        public VoxelStrength Strength;

        public VoxelMaterialEntry(Material material, VoxelStrength strength)
        {
            Material = material;
            Strength = strength;
        }
    }
}
