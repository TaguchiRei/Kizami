using System;
using Unity.Mathematics;
using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 事前ベイクした 1 パーツ分の SDF。
    /// サンプル s のローカル座標は Origin + s * VoxelSize。距離は VoxelSdfEncoding で量子化して持つ。
    /// </summary>
    [Serializable]
    public sealed class VoxelSdfData
    {
        [SerializeField]
        [Tooltip("読み込み先 Transform の、VoxelModelLoader からの相対パス。空ならローダー自身")]
        private string _path;

        [SerializeField] private Vector3 _origin;
        [SerializeField] private Vector3Int _sampleCount;
        [SerializeField] private float _voxelSize;
        [SerializeField] private float _maxDistance;
        [SerializeField] private short[] _samples;

        public VoxelSdfData(string path, Vector3 origin, Vector3Int sampleCount, float voxelSize, float maxDistance,
            short[] samples)
        {
            _path = path;
            _origin = origin;
            _sampleCount = sampleCount;
            _voxelSize = voxelSize;
            _maxDistance = maxDistance;
            _samples = samples;
        }

        public string Path => _path;
        public float3 Origin => _origin;
        public int3 SampleCount => new(_sampleCount.x, _sampleCount.y, _sampleCount.z);
        public float VoxelSize => _voxelSize;

        /// <summary> 量子化の範囲。これより遠い距離は丸められている </summary>
        public float MaxDistance => _maxDistance;

        public short[] Samples => _samples;

        /// <summary> 端のサンプル同士を結ぶ、ローカル空間の範囲 </summary>
        public VoxelBounds LocalBounds => new(Origin, Origin + (float3)(SampleCount - 1) * _voxelSize);
    }
}
