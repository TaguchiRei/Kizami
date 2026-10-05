using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 近接切断の振り（Swing）を受けて、カメラの位置と向き、切断面の角度から刃を配置し、
    /// 範囲内の切れる CuttableObject をまとめて切断する。切断の結果はログに出す。
    /// 刃の位置はカメラの位置、法線はカメラの上方向をカメラの前方向を軸に角度だけ回したもの。
    /// 範囲は、カメラの前方へ伸びる、刃に沿った薄い直方体。
    /// </summary>
    public sealed class MeleeCutAdapter : InitializableMonoBehaviour
    {
        /// <summary> 範囲内から一度に集めるコライダーの最大数 </summary>
        private const int MaxHitCount = 128;

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

        private readonly Collider[] _hitBuffer = new Collider[MaxHitCount];
        private readonly List<CuttableObject> _targets = new();

        /// <summary> 切断を実行中か。実行中の振りは無視する </summary>
        private bool _isCutting;

        /// <summary>
        /// PlayerInitializer から呼ばれる。
        /// </summary>
        public override void Initialize()
        {
            if (_blade == null)
            {
                UsefulLogger.LogError("MultiCutBlade が設定されていません。", this);
            }

            base.Initialize();
        }

        /// <summary>
        /// 振ったときの角度で刃を配置し、範囲内の切れる対象を切断する。
        /// 切断の実行中と、対象が 1 つもないときは何もしない。
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
            if (_targets.Count == 0) return;

            _blade.transform.SetPositionAndRotation(origin, bladeRotation);
            CutAsync(_targets.ToArray(), angle).Forget();
        }

        /// <summary>
        /// 範囲内にあり、今切れる状態で、Renderer のバウンディングボックスが刃の平面をまたぐ CuttableObject を集める。
        /// </summary>
        private void CollectTargets(Vector3 origin, Vector3 forward, Vector3 normal, Quaternion bladeRotation)
        {
            _targets.Clear();

            var center = origin + forward * (_reach * 0.5f);
            var halfExtents = new Vector3(_width * 0.5f, _thickness * 0.5f, _reach * 0.5f);
            var hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, _hitBuffer, bladeRotation,
                _targetLayers, QueryTriggerInteraction.Ignore);

            if (hitCount == _hitBuffer.Length)
            {
                UsefulLogger.LogWarning($"切断の範囲内のコライダーが上限（{MaxHitCount}）に達しました。", this);
            }

            for (var i = 0; i < hitCount; i++)
            {
                if (!_hitBuffer[i].TryGetComponent(out CuttableObject cuttable)) continue;
                if (!cuttable.IsCuttable || _targets.Contains(cuttable)) continue;
                if (!IsStraddlingPlane(cuttable, origin, normal)) continue;

                _targets.Add(cuttable);
            }
        }

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

        /// <summary>
        /// 対象を切断し、終わったら結果をログに出す。
        /// </summary>
        private async UniTaskVoid CutAsync(CuttableObject[] targets, float angle)
        {
            _isCutting = true;

            try
            {
                var results = await _blade.ExecuteCut(targets);
                LogResults(results, angle);
            }
            finally
            {
                _isCutting = false;
            }
        }

        /// <summary>
        /// 切断した元の対象と、表と裏のかけらの組をログに出す。親の中での並び順を添えて、同じ名前のかけらを区別する。
        /// </summary>
        private void LogResults(MultiCutResult[] results, float angle)
        {
            var builder = new StringBuilder();
            builder.Append($"角度 {angle}° で {results.Length} 個を切断しました。");

            foreach (var result in results)
            {
                builder.Append($"\n  元: {Describe(result.Original)} / 表: {Describe(result.Front)} / 裏: {Describe(result.Back)}");
            }

            UsefulLogger.Log(builder.ToString(), this);
        }

        private static string Describe(CuttableObject cuttable)
        {
            return $"{cuttable.name}#{cuttable.transform.GetSiblingIndex()}";
        }
    }
}
