using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// MainCamera を基準にした形（狙った所の球、視線の向きへ伸びるカプセル、カメラの位置を中心にした球）の範囲にあるボクセルのピースを、同じ形でまとめて削る Adapter。
    /// 範囲が複数のピースにまたがるときも、範囲内のすべてのピースを削る。
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
        /// 狙った所を球で削る。狙える距離に何もなければ削らない。
        /// </summary>
        public void CarveAtAim()
        {
            if (!TryGetCameraTransform(out var cameraTransform)) return;

            if (!Physics.Raycast(cameraTransform.position, cameraTransform.forward, out var hit, _maxDistance,
                    _targetLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            var hitCount = Physics.OverlapSphereNonAlloc(hit.point, _radius, _hitBuffer, _targetLayers,
                QueryTriggerInteraction.Ignore);
            Carve(new SphereShape(hit.point, _radius), hitCount);
        }

        /// <summary>
        /// カメラの位置から視線の向きへ伸びるカプセルで削る。途中の壁を貫通する。
        /// </summary>
        /// <param name="length">カプセルの長さ（m）</param>
        /// <param name="radius">カプセルの半径（m）</param>
        public void CarveBeam(float length, float radius)
        {
            if (!TryGetCameraTransform(out var cameraTransform)) return;

            var start = cameraTransform.position;
            var end = start + cameraTransform.forward * length;
            var hitCount = Physics.OverlapCapsuleNonAlloc(start, end, radius, _hitBuffer, _targetLayers,
                QueryTriggerInteraction.Ignore);
            Carve(new CapsuleShape(start, end, radius), hitCount);
        }

        /// <summary>
        /// カメラの位置を中心に球で削る。足場がボクセルなら足場も削れる。
        /// </summary>
        /// <param name="radius">球の半径（m）</param>
        public void CarveExplosion(float radius)
        {
            if (!TryGetCameraTransform(out var cameraTransform)) return;

            var center = cameraTransform.position;
            var hitCount = Physics.OverlapSphereNonAlloc(center, radius, _hitBuffer, _targetLayers,
                QueryTriggerInteraction.Ignore);
            Carve(new SphereShape(center, radius), hitCount);
        }

        private bool TryGetCameraTransform(out Transform cameraTransform)
        {
            var cameraMain = Camera.main;
            if (cameraMain == null)
            {
                UsefulLogger.LogError("MainCamera が見つからない為、削れません。", this);
                cameraTransform = null;
                return false;
            }

            cameraTransform = cameraMain.transform;
            return true;
        }

        /// <summary>
        /// 範囲の問い合わせで集めたコライダーからピースを集め、ワールド空間の形で削る。
        /// </summary>
        /// <param name="shape">削る形（ワールド空間）</param>
        /// <param name="hitCount">_hitBuffer に集めたコライダーの数</param>
        private void Carve<TShape>(in TShape shape, int hitCount)
            where TShape : struct, ITransformableVoxelShape<TShape>
        {
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

            foreach (var piece in _pieces)
            {
                piece.ApplyEdit(shape, VoxelCsgOperation.Subtract, Space.World);
            }

            _pieces.Clear();
        }
    }
}
