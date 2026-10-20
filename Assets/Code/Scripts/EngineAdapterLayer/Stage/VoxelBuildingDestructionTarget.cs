using System.Text;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 階ごとのボクセルでできた建物（VoxelBuilding）を破壊対象にする。VoxelBuilding と同じ GameObject に付け、ステージシーンへ置く。
    /// 建物のボクセルの体積が、読み込んだ時点から必要な割合以上削れたら破壊済みとする。
    /// </summary>
    [RequireComponent(typeof(VoxelBuilding))]
    public sealed class VoxelBuildingDestructionTarget : MonoBehaviour, IDestructionTarget
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("建物の、読み込んだ時点の体積のうち、削れている必要がある割合")]
        private float _requiredRatio = 0.3f;

        private VoxelBuilding _building;

        /// <summary> 建物の体積のうち削れている必要がある割合 </summary>
        public float RequiredRatio => _requiredRatio;

        /// <summary> 建物の体積が必要な割合以上削れているか。一度もボクセルにしていない建物は false </summary>
        public bool IsDestroyed => Building.LostVolumeRatio >= _requiredRatio;

        /// <summary> 建物の、読み込んだ時点の範囲の中心 </summary>
        public Vector3 Center => Building.InitialCenter;

        private VoxelBuilding Building => _building != null ? _building : _building = GetComponent<VoxelBuilding>();

        /// <summary>
        /// 名前と、削れた割合、必要な割合を書き足す。
        /// </summary>
        public void AppendStatus(StringBuilder builder)
        {
            builder.Append(name).Append(": ").Append(Building.LostVolumeRatio.ToString("F2"))
                .Append(" / ").Append(_requiredRatio.ToString("F2"));
        }
    }
}
