using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 融解した粒、または固体の一部が蒸発したことを表す。
    /// </summary>
    public readonly struct VoxelEvaporation
    {
        /// <summary> 蒸発した位置（ワールド空間） </summary>
        public readonly Vector3 Position;

        /// <summary> 蒸発した体積（ワールド空間, m³） </summary>
        public readonly float Volume;

        public VoxelEvaporation(Vector3 position, float volume)
        {
            Position = position;
            Volume = volume;
        }
    }
}
