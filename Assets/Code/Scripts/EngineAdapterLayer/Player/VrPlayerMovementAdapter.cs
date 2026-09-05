using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// VR 用の移動・視点反映。視点は左右の連続旋回だけを XR Origin へ反映する。
    ///
    /// 上下方向は HMD の姿勢が担う為、入力値の y は捨てる。この打ち切りが VR 固有の仕様であり、
    /// State と Application 側は上下の入力を載せたままで構わない。
    /// 視点入力はスティックの倒し量として扱う為、旋回速度とみなして経過時間で積分する。
    /// XR Origin を回すと、その子であるカメラの姿勢に旋回分が加算される。
    /// </summary>
    public sealed class VrPlayerMovementAdapter : PlayerMovementAdapterBase
    {
        [Header("視点")]
        [SerializeField]
        [Tooltip("旋回を反映する Transform。XR Origin。")]
        private Transform _xrOriginTransform;

        [SerializeField, Min(0f)]
        [Tooltip("感度倍率 1.0 でスティックを倒し切ったときの旋回速度（度/秒）")]
        private float _degreesPerSecond = 90f;

        /// <summary> 現在の旋回入力。入力イベントで更新し、毎フレーム積分する </summary>
        private float _turnInput;

        private float _yaw;

        public override void Initialize()
        {
            base.Initialize();

            if (_xrOriginTransform == null)
            {
                UsefulLogger.LogError("XR Origin の Transform が設定されていません。", this);
            }
        }

        protected override void OnLookInputChanged(Vector2 lookInput)
        {
            // 上下方向は HMD が担う為、ここで捨てる
            _turnInput = lookInput.x;
        }

        private void Update()
        {
            if (_xrOriginTransform == null) return;
            if (Mathf.Approximately(_turnInput, 0f)) return;

            _yaw = Mathf.Repeat(_yaw + _turnInput * _degreesPerSecond * Time.deltaTime, 360f);
            _xrOriginTransform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }
    }
}
