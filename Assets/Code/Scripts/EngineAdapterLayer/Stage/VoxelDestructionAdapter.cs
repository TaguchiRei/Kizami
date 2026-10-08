using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// MainCamera を基準にした形（狙った所の球、視線の向きへ伸びるカプセル、カメラの位置を中心にした球）の範囲にあるボクセルのピースを、同じ形でまとめて削る Adapter。
    /// 範囲が複数のピースにまたがるときも、範囲内のすべてのピースを削る。
    /// 破壊タイプの攻撃として、装甲のパネルに当たったら 1 回当たったものとし、装甲の奥のピースは削らない。
    /// </summary>
    public sealed class VoxelDestructionAdapter : MonoBehaviour
    {
        /// <summary> 削る範囲から一度に集めるコライダーの最大数 </summary>
        private const int MAX_HIT_COUNT = 64;

        private readonly Collider[] _hitBuffer = new Collider[MAX_HIT_COUNT];
        private readonly Collider[] _armorHitBuffer = new Collider[MAX_HIT_COUNT];
        private readonly HashSet<VoxelPiece> _pieces = new();
        private readonly HashSet<ArmorPanel> _armorPanels = new();

        [SerializeField, Min(0f)]
        [Tooltip("狙える最大の距離（m）")]
        private float _maxDistance = 50f;

        [SerializeField, Min(0.01f)]
        [Tooltip("削る球の半径（m）")]
        private float _radius = 1.5f;

        [SerializeField]
        [Tooltip("狙う先と、削るピースを探すレイヤー")]
        private LayerMask _targetLayers = ~0;

        [SerializeField]
        [Tooltip("装甲のパネルのレイヤー。遮断の判定と、当たったパネルの収集に使う")]
        private LayerMask _armorLayers;

        /// <summary>
        /// 狙った所を球で削る。狙える距離に何もなければ削らない。
        /// レイが最初に当たったのが装甲のパネルなら、そのパネルに 1 回当てて削らない。
        /// </summary>
        public void CarveAtAim()
        {
            if (!TryGetCameraTransform(out var cameraTransform)) return;

            if (!Physics.Raycast(cameraTransform.position, cameraTransform.forward, out var hit, _maxDistance,
                    _targetLayers | _armorLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (hit.collider.TryGetComponent(out ArmorPanel armorPanel))
            {
                armorPanel.ApplyHit();
                return;
            }

            var hitCount = Physics.OverlapSphereNonAlloc(hit.point, _radius, _hitBuffer, _targetLayers,
                QueryTriggerInteraction.Ignore);
            Carve(new SphereShape(hit.point, _radius), hitCount);
        }

        /// <summary>
        /// カメラの位置から視線の向きへ伸びるカプセルで削る。途中の壁を貫通する。
        /// 中心のレイが装甲のパネルに当たったら、そのパネルに 1 回当て、カプセルの先端がその点に届く所までで止める。
        /// </summary>
        /// <param name="length">カプセルの長さ（m）</param>
        /// <param name="radius">カプセルの半径（m）</param>
        public void CarveBeam(float length, float radius)
        {
            if (!TryGetCameraTransform(out var cameraTransform)) return;

            var start = cameraTransform.position;
            var forward = cameraTransform.forward;

            if (Physics.Raycast(start, forward, out var armorHit, length, _armorLayers, QueryTriggerInteraction.Ignore))
            {
                if (armorHit.collider.TryGetComponent(out ArmorPanel armorPanel)) armorPanel.ApplyHit();

                // カプセルの端の半球が装甲を越えないよう、中心の線を半径の分だけ手前で止める
                length = armorHit.distance - radius;
                if (length <= 0f) return;
            }

            var end = start + forward * length;
            var hitCount = Physics.OverlapCapsuleNonAlloc(start, end, radius, _hitBuffer, _targetLayers,
                QueryTriggerInteraction.Ignore);
            Carve(new CapsuleShape(start, end, radius), hitCount);
        }

        /// <summary>
        /// カメラの位置を中心に球で削る。足場がボクセルなら足場も削れる。
        /// 球に入った装甲のパネルすべてに 1 回ずつ当て、中心から見て装甲の奥にあるピースは削らない。
        /// 遮断は中心から各ピースのバウンディングボックスの中心へのレイで判定するので、ピースの一部だけが装甲の陰にあっても、ピース全体を削るか削らないかのどちらかになる。
        /// </summary>
        /// <param name="radius">球の半径（m）</param>
        public void CarveExplosion(float radius)
        {
            if (!TryGetCameraTransform(out var cameraTransform)) return;

            var center = cameraTransform.position;
            CollectArmorPanels(center, radius);

            var hitCount = Physics.OverlapSphereNonAlloc(center, radius, _hitBuffer, _targetLayers,
                QueryTriggerInteraction.Ignore);

            // 遮断の判定は、当てて壊れる前のパネルで行う
            Carve(new SphereShape(center, radius), hitCount, _armorPanels.Count > 0, center);

            foreach (var armorPanel in _armorPanels)
            {
                armorPanel.ApplyHit();
            }

            _armorPanels.Clear();
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
        /// 球に入った装甲のパネルを _armorPanels に集める。
        /// </summary>
        private void CollectArmorPanels(Vector3 center, float radius)
        {
            _armorPanels.Clear();

            var hitCount = Physics.OverlapSphereNonAlloc(center, radius, _armorHitBuffer, _armorLayers,
                QueryTriggerInteraction.Ignore);

            if (hitCount == _armorHitBuffer.Length)
            {
                UsefulLogger.LogWarning($"範囲内の装甲のコライダーが上限（{MAX_HIT_COUNT}）に達しました。", this);
            }

            for (var i = 0; i < hitCount; i++)
            {
                if (_armorHitBuffer[i].TryGetComponent(out ArmorPanel armorPanel)) _armorPanels.Add(armorPanel);
            }
        }

        /// <summary>
        /// 範囲の問い合わせで集めたコライダーからピースを集め、ワールド空間の形で削る。
        /// </summary>
        /// <param name="shape">削る形（ワールド空間）</param>
        /// <param name="hitCount">_hitBuffer に集めたコライダーの数</param>
        /// <param name="checkArmor">true なら、origin から見て装甲の奥にあるピースを削らない</param>
        /// <param name="origin">装甲の遮断を判定するレイの始点</param>
        private void Carve<TShape>(in TShape shape, int hitCount, bool checkArmor = false, Vector3 origin = default)
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
                if (checkArmor && IsBehindArmor(origin, piece.WorldBounds.center)) continue;

                piece.ApplyEdit(shape, VoxelCsgOperation.Subtract, Space.World);
            }

            _pieces.Clear();
        }

        /// <summary>
        /// origin から target へのレイが、途中で装甲のパネルに当たるか。
        /// </summary>
        private bool IsBehindArmor(Vector3 origin, Vector3 target)
        {
            var toTarget = target - origin;
            return Physics.Raycast(origin, toTarget, toTarget.magnitude, _armorLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
