using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 一人称の腕を動かす Adapter。腕はカメラの子に置く。
    /// 肩をカメラから見た決まった位置に置き、IK の目標で手の位置を決める。待機では手を構えの位置に置き、
    /// 通常攻撃の振りでは、目標を切断面に沿って往復させて刃を持つ手を振る。
    /// 時間の倍率に影響されないよう、経過時間は実時間で数える。
    /// </summary>
    public sealed class PlayerArmAdapter : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("IK を効かせる Rig")]
        private Rig _rig;

        [SerializeField]
        [Tooltip("振る手の IK の目標")]
        private Transform _swingTarget;

        [SerializeField]
        [Tooltip("振る腕の肩の骨。カメラから見た ShoulderPosition に置く")]
        private Transform _shoulder;

        [SerializeField]
        [Tooltip("肩を置く、カメラから見た位置（m）。画面の外になる下と後ろに置く")]
        private Vector3 _shoulderPosition = new(0.3f, -0.45f, -0.2f);

        [SerializeField]
        [Tooltip("手を構える、カメラから見た位置（m）")]
        private Vector3 _readyPosition = new(0.2f, -0.2f, 0.35f);

        [SerializeField, Min(0f)]
        [Tooltip("振りの幅の半分（m）。構えの位置から切断面に沿って、この距離だけ両側へ振る")]
        private float _swingRadius = 0.3f;

        [SerializeField, Min(0f)]
        [Tooltip("振りで手が前へ突き出る距離（m）。振りの中ほどで最大になる")]
        private float _swingReach = 0.1f;

        [SerializeField, Min(0.01f)]
        [Tooltip("1 回の振りにかかる時間（秒）")]
        private float _swingDuration = 0.15f;

        private Transform _view;
        private Quaternion _readyRotation;
        private float _swingAngle;
        private float _swingElapsed;
        private bool _isSwinging;

        /// <summary>
        /// 切断面の角度で、手を振る。振っている途中なら、最初から振り直す。
        /// </summary>
        /// <param name="angle">切断面の角度（度）。0 で水平に振る</param>
        public void PlaySwing(float angle)
        {
            if (_rig == null || _swingTarget == null) return;

            _swingAngle = angle;
            _swingElapsed = 0f;
            _isSwinging = true;
        }

        private void Awake()
        {
            _view = transform.parent;
            if (_view == null || _shoulder == null || _swingTarget == null || _rig == null) return;

            // 肩の骨を、カメラから見た決まった位置に合わせる
            transform.position += _view.TransformPoint(_shoulderPosition) - _shoulder.position;

            // 構えの向きは、骨の初期の姿勢の手の向きをカメラから見た向きにしたもの
            _readyRotation = Quaternion.Inverse(_view.rotation) * _swingTarget.rotation;
            _rig.weight = 1f;
            Place(Vector3.zero, Quaternion.identity);
        }

        private void Update()
        {
            if (!_isSwinging || _view == null || _swingTarget == null) return;

            _swingElapsed += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(_swingElapsed / _swingDuration);
            var eased = Mathf.SmoothStep(0f, 1f, progress);

            // カメラの前方まわりに angle だけ回した「右」が、切断面の上で刃が進む向き
            var roll = Quaternion.AngleAxis(_swingAngle, Vector3.forward);
            var offset = roll * Vector3.right * Mathf.Lerp(-_swingRadius, _swingRadius, eased) +
                         Vector3.forward * (Mathf.Sin(progress * Mathf.PI) * _swingReach);
            Place(offset, roll);

            if (progress >= 1f)
            {
                _isSwinging = false;
                Place(Vector3.zero, Quaternion.identity);
            }
        }

        /// <summary>
        /// IK の目標を、構えの位置と向きからの差で置く。差はカメラから見た値。
        /// </summary>
        private void Place(Vector3 offset, Quaternion roll)
        {
            _swingTarget.SetPositionAndRotation(_view.TransformPoint(_readyPosition + offset),
                _view.rotation * (roll * _readyRotation));
        }
    }
}
