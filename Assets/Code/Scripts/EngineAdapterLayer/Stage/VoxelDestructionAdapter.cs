using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// MainCamera の中心から撃ったレイが当たった所を中心に、球の範囲にあるボクセルのピースをまとめて削る Adapter。
    /// 範囲が複数のピースにまたがるときも、当たったピースだけでなく範囲内のすべてのピースを削る。
    /// </summary>
    public sealed class VoxelDestructionAdapter : MonoBehaviour
    {
        /// <summary> 削る範囲から一度に集めるコライダーの最大数 </summary>
        private const int MAX_HIT_COUNT = 64;

        private readonly Collider[] _hitBuffer = new Collider[MAX_HIT_COUNT];
        private readonly HashSet<VoxelPiece> _pieces = new();

        [SerializeField, Min(0f)]
        [Tooltip("狙える最大の距離（m）")]
        private float _maxDistance = 50f;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る球の半径（m）")]
        private float _radius = 1.5f;

        [SerializeField]
        [Tooltip("狙う先と、削るピースを探すレイヤー")]
        private LayerMask _targetLayers = ~0;

        /// <summary>
        /// 狙った所を削る。狙える距離に何もなければ削らない。
        /// </summary>
        public void CarveAtAim()
        {
            var cameraMain = Camera.main;
            if (cameraMain == null)
            {
                UsefulLogger.LogError("MainCamera が見つからない為、削れません。", this);
                return;
            }

            var cameraTransform = cameraMain.transform;
            if (!Physics.Raycast(cameraTransform.position, cameraTransform.forward, out var hit, _maxDistance,
                    _targetLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Carve(hit.point);
        }

        /// <summary>
        /// 中心から半径の範囲にコライダーを持つピースを集め、同じ球で削る。
        /// </summary>
        private void Carve(Vector3 center)
        {
            var hitCount = Physics.OverlapSphereNonAlloc(center, _radius, _hitBuffer, _targetLayers,
                QueryTriggerInteraction.Ignore);

            if (hitCount == _hitBuffer.Length)
            {
                UsefulLogger.LogWarning($"削る範囲内のコライダーが上限（{MAX_HIT_COUNT}）に達しました。", this);
            }

            _pieces.Clear();
            for (var i = 0; i < hitCount; i++)
            {
                var piece = _hitBuffer[i].GetComponentInParent<VoxelPiece>();
                if (piece != null) _pieces.Add(piece);
            }

            var shape = new SphereShape(center, _radius);
            foreach (var piece in _pieces)
            {
                piece.ApplyEdit(shape, VoxelCsgOperation.Subtract, Space.World);
            }

            _pieces.Clear();
        }
    }
}
