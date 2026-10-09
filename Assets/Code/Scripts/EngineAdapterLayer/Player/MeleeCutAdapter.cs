using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 近接切断の振り（Swing）を受けて、カメラの位置と向き、切断面の角度から刃を配置し、
    /// カメラの前方へ伸びる薄い直方体の範囲にある切れる CuttableObject をまとめて切断する Adapter。
    /// 攻撃タイプの攻撃として、範囲にある装甲のパネルには 1 回ずつ当てる。パネルは切断しないので、かけらは出ない。
    /// カメラから見て装甲の奥にある対象（ディフェンダーのバリアの中の敵など）は切らない。
    /// </summary>
    public sealed class MeleeCutAdapter : InitializableMonoBehaviour
    {
        /// <summary> 範囲内から一度に集めるコライダーの最大数 </summary>
        private const int MAX_HIT_COUNT = 128;

        private readonly Collider[] _hitBuffer = new Collider[MAX_HIT_COUNT];
        private readonly List<CuttableObject> _targets = new();
        private readonly HashSet<ArmorPanel> _armorPanels = new();

        [SerializeField]
        [Tooltip("切断に使う刃。プールと同じシーンに置かれたもの")]
        private MultiCutBlade _blade;

        [SerializeField, Min(0f)]
        [Tooltip("切断の範囲の、カメラから前方への距離（m）")]
        private float _reach = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("切断の範囲の、刃に沿った横方向の幅（m）")]
        private float _width = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("切断の範囲の、刃の法線方向の厚み（m）")]
        private float _thickness = 0.2f;

        [SerializeField]
        [Tooltip("切断の対象を探すレイヤー")]
        private LayerMask _targetLayers = ~0;

        [SerializeField]
        [Tooltip("装甲のレイヤー。カメラから見てこのレイヤーの当たり判定の奥にある対象は切らない")]
        private LayerMask _armorLayers;

        /// <summary> 切断の結果と、振ったときの切断面（ワールド座標）を渡す先 </summary>
        private Action<MultiCutResult[], Plane> _onCut;

        /// <summary> 切断を実行中か。実行中の振りは無視する </summary>
        private bool _isCutting;

        /// <summary>
        /// Renderer のバウンディングボックスが、指定した点を通り法線に垂直な平面をまたぐかどうか。
        /// 平面と交わらない対象を切ると、中身がすべて入ったかけらと空のかけらができる為、切る前に除く。
        /// </summary>
        private static bool IsStraddlingPlane(CuttableObject cuttable, Vector3 planePoint, Vector3 normal)
        {
            if (cuttable.Renderer == null) return false;

            var bounds = cuttable.Renderer.bounds;
            var extents = bounds.extents;
            var radius = Mathf.Abs(normal.x) * extents.x + Mathf.Abs(normal.y) * extents.y +
                         Mathf.Abs(normal.z) * extents.z;
            var distance = Vector3.Dot(normal, bounds.center - planePoint);

            return Mathf.Abs(distance) < radius;
        }

        /// <param name="onCut">切断が終わったときに、切断の結果と切断面を渡す関数。切断面の法線は表のかけらの側を向く</param>
        public void Initialize(Action<MultiCutResult[], Plane> onCut)
        {
            _onCut = onCut;

            if (_blade == null)
            {
                UsefulLogger.LogError("MultiCutBlade が設定されていません。", this);
            }

            base.Initialize();
        }

        /// <summary>
        /// 振ったときの角度で刃を配置し、範囲内の切れる対象を切断する。範囲内の装甲のパネルには 1 回ずつ当てる。
        /// </summary>
        /// <param name="angle">切断面の角度（度）</param>
        public void Swing(float angle)
        {
            if (!Initialized || _blade == null) return;

            if (_isCutting)
            {
                UsefulLogger.Log("前の切断を実行中の為、今回の振りでは切断しませんでした。", this);
                return;
            }

            var cameraMain = Camera.main;
            if (cameraMain == null)
            {
                UsefulLogger.LogError("MainCamera が見つからない為、切断できません。", this);
                return;
            }

            var cameraTransform = cameraMain.transform;
            var origin = cameraTransform.position;
            var forward = cameraTransform.forward;
            var normal = Quaternion.AngleAxis(angle, forward) * cameraTransform.up;
            var bladeRotation = Quaternion.LookRotation(forward, normal);

            CollectTargets(origin, forward, normal, bladeRotation);

            foreach (var armorPanel in _armorPanels)
            {
                armorPanel.ApplyHit();
            }

            _armorPanels.Clear();
            if (_targets.Count == 0) return;

            _blade.transform.SetPositionAndRotation(origin, bladeRotation);
            CutAsync(_targets.ToArray(), new Plane(bladeRotation * Vector3.up, origin)).Forget();
        }

        /// <summary>
        /// 範囲内にあり、今切れる状態で、Renderer のバウンディングボックスが刃の平面をまたぎ、装甲の奥にない CuttableObject を _targets に集める。
        /// 範囲内の装甲のパネルは _armorPanels に集める。
        /// </summary>
        private void CollectTargets(Vector3 origin, Vector3 forward, Vector3 normal, Quaternion bladeRotation)
        {
            _targets.Clear();
            _armorPanels.Clear();

            var center = origin + forward * (_reach * 0.5f);
            var halfExtents = new Vector3(_width * 0.5f, _thickness * 0.5f, _reach * 0.5f);
            var hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, _hitBuffer, bladeRotation,
                _targetLayers, QueryTriggerInteraction.Ignore);

            if (hitCount == _hitBuffer.Length)
            {
                UsefulLogger.LogWarning($"切断の範囲内のコライダーが上限（{MAX_HIT_COUNT}）に達しました。", this);
            }

            for (var i = 0; i < hitCount; i++)
            {
                if (_hitBuffer[i].TryGetComponent(out ArmorPanel armorPanel))
                {
                    _armorPanels.Add(armorPanel);
                    continue;
                }

                if (!_hitBuffer[i].TryGetComponent(out CuttableObject cuttable)) continue;
                if (!cuttable.IsCuttable || _targets.Contains(cuttable)) continue;
                if (!IsStraddlingPlane(cuttable, origin, normal) || IsBehindArmor(origin, cuttable)) continue;

                _targets.Add(cuttable);
            }
        }

        /// <summary>
        /// カメラの位置 origin から対象の Renderer のバウンディングボックスの中心までの間に、装甲の当たり判定があるか。
        /// </summary>
        private bool IsBehindArmor(Vector3 origin, CuttableObject cuttable)
        {
            var toTarget = cuttable.Renderer.bounds.center - origin;
            return Physics.Raycast(origin, toTarget, toTarget.magnitude, _armorLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 対象を切断し、終わったら結果と切断面を渡す。
        /// </summary>
        private async UniTaskVoid CutAsync(CuttableObject[] targets, Plane plane)
        {
            _isCutting = true;

            try
            {
                var results = await _blade.ExecuteCut(targets);
                _onCut?.Invoke(results, plane);
            }
            finally
            {
                _isCutting = false;
            }
        }
    }
}
