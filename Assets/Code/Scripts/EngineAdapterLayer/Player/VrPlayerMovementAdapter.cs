using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// VR 用の移動・視点反映。
    /// 上下方向は HMD の姿勢が担うので、入力値の y は捨て、左右の連続旋回だけを XR Origin へ反映する。
    /// 視点入力はスティックの倒し量なので、旋回速度とみなして経過時間で積分する。
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

            _yaw = Mathf.Repeat(_yaw + _turnInput * _degreesPerSecond * Time.unscaledDeltaTime, 360f);
            _xrOriginTransform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }
    }
}
