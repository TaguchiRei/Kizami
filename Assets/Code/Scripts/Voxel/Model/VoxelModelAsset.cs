using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kizami.Voxel
{
    /// <summary>
    /// モデル 1 つ分の、事前ベイクした SDF パーツの集まり。
    /// 距離データが大きい為、テキストではなくバイナリで保存する。
    /// </summary>
    [Icon("Assets/Art/Textures/BoxelIcon.png")]
    [PreferBinarySerialization]
    public sealed class VoxelModelAsset : ScriptableObject
    {
        [SerializeField] private VoxelSdfData[] _parts = Array.Empty<VoxelSdfData>();

        public IReadOnlyList<VoxelSdfData> Parts => _parts;

        /// <summary>
        /// パーツを差し替える。ベイク処理から呼ぶ。
        /// </summary>
        public void SetParts(VoxelSdfData[] parts)
        {
            _parts = parts;
        }
    }
}
