using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// PC / スマホ用の移動・視点（左右方向）反映。
    /// 移動方向が体の向きに従うので、体には水平方向（Yaw）だけを反映し、上下方向（Pitch）は StandardPlayerCameraAdapter が反映する。
    /// 視点入力はそのフレーム分の移動量なので、経過時間で割らずに届いたその場で回転へ加算する。
    /// </summary>
    public sealed class StandardPlayerMovementAdapter : PlayerMovementAdapterBase
    {
        [Header("視点")]
        [SerializeField]
        [Tooltip("視線の向きの取得元。CinemachineCamera。体の子である必要がある。")]
        private Transform _cameraTransform;

        [SerializeField, Min(0f)]
        [Tooltip("感度倍率 1.0 のときの、入力 1 単位あたりの回転角（度）")]
        private float _degreesPerInput = 0.1f;

        private float _yaw;

        public override void Initialize()
        {
            base.Initialize();

            if (_cameraTransform == null)
            {
                UsefulLogger.LogError("カメラの Transform が設定されていません。", this);
            }
        }

        /// <summary>
        /// カメラの前方を視線の向きとして返す。カメラが未設定ならこの Transform の前方を返す。
        /// </summary>
        protected override Vector3 GetViewDirection()
        {
            return _cameraTransform != null ? _cameraTransform.forward : transform.forward;
        }

        protected override void OnLookInputChanged(Vector2 lookInput)
        {
            if (lookInput.x == 0f) return;

            _yaw = Mathf.Repeat(_yaw + lookInput.x * _degreesPerInput, 360f);
            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }
    }
}
