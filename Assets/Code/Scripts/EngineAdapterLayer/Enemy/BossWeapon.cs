using System;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスの武装1つの設定。種類と、武装を形づくるボクセルのパーツのパスからなる。
    /// </summary>
    [Serializable]
    public sealed class BossWeapon
    {
        [SerializeField]
        [Tooltip("武装の種類")]
        private BossWeaponKind _kind;

        [SerializeField]
        [Tooltip("武装を形づくるパーツの、VoxelModelLoader からの相対パス")]
        private string[] _partPaths = Array.Empty<string>();

        /// <summary> 武装の種類 </summary>
        public BossWeaponKind Kind => _kind;

        /// <summary> 武装を形づくるパーツの、VoxelModelLoader からの相対パス </summary>
        public string[] PartPaths => _partPaths;
    }
}
