using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// まとめてベイクするモデルのパーツに付け、そのパーツの素材の強度を指定する。
    /// 素材のマテリアルは、同じ GameObject の MeshRenderer のマテリアルを使う。付いていないパーツは普通の強度になる。
    /// </summary>
    public sealed class VoxelBakeMaterial : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("このパーツの素材の強度")]
        private VoxelStrength _strength = VoxelStrength.Normal;

        public VoxelStrength Strength => _strength;
    }
}
