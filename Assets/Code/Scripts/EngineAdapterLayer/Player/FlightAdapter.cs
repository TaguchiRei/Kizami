using System;
using Kizami.BlackBoard;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 毎フレーム、カメラの視線の先にある飛び移りの目印（FlightAnchor）を探して報告する Adapter。
    /// 目印へ向かう向きと視線のなす角が最も小さい目印を選ぶ。遮る物は調べない。
    /// </summary>
    public sealed class FlightAdapter : MonoBehaviour
    {
        [SerializeField, Min(1f)]
        [Tooltip("目印を探す、カメラからの距離（m）")]
        private float _range = 400f;

        [SerializeField, Range(1f, 30f)]
        [Tooltip("視線と目印へ向かう向きのなす角がこの角度（度）以下なら、視線の先にあるとみなす")]
        private float _aimAngle = 8f;

        private Action<FlightTarget?> _report;

        /// <param name="report">毎フレーム、見つけた目印の飛び先か null を渡す関数</param>
        public void Initialize(Action<FlightTarget?> report)
        {
            _report = report;
        }

        private void Update()
        {
            if (_report == null) return;

            var cameraMain = Camera.main;
            if (cameraMain == null)
            {
                _report(null);
                return;
            }

            var cameraTransform = cameraMain.transform;
            FlightTarget? best = null;
            var bestAngle = _aimAngle;
            foreach (var anchor in FlightAnchor.Anchors)
            {
                if (!anchor.IsAvailable) continue;

                var target = anchor.GetTarget();

                var toTarget = target.Position - cameraTransform.position;
                if (toTarget.sqrMagnitude > _range * _range) continue;

                var angle = Vector3.Angle(cameraTransform.forward, toTarget);
                if (angle > bestAngle) continue;

                best = target;
                bestAngle = angle;
            }

            _report(best);
        }
    }
}
