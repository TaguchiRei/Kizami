using Unity.Cinemachine;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// PC / スマホ用の移動・視点反映。
    /// 視点は水平方向を体に、上下方向を Cinemachine カメラの Tilt 軸に入れる。体を上下に傾けないのは、
    /// 移動方向が体の向きに従う為。上下方向の可動域は CinemachinePanTilt.TiltAxis.Range（Inspector）で管理する。
    ///
    /// 視点入力はマウスや指の移動量として扱う為、届いたその場で回転へ加算する。
    /// 経過時間では割らない（移動量そのものが既にそのフレーム分の量である為）。
    /// </summary>
    public sealed class StandardPlayerMovementAdapter : PlayerMovementAdapterBase
    {
        [Header("視点")]
        [SerializeField]
        [Tooltip("移動方向の基準に使う Transform。CinemachineCamera。体の子である必要がある。")]
        private Transform _cameraTransform;

        [SerializeField]
        [Tooltip("上下方向（Tilt）の回転を反映するコンポーネント。_cameraTransform と同じオブジェクトに付いている必要がある。")]
        private CinemachinePanTilt _panTilt;

        [SerializeField, Min(0f)]
        [Tooltip("感度倍率 1.0 のときの、入力 1 単位あたりの回転角（度）")]
        private float _degreesPerInput = 0.1f;

        private float _yaw;

        public override void Initialize()
        {
            base.Initialize();

            if (_cameraTransform == null || _panTilt == null)
            {
                UsefulLogger.LogError("体またはカメラの Transform / CinemachinePanTilt が設定されていません。", this);
            }
        }

        /// <summary>
        /// カメラの向く水平方向を前方として、入力方向（x が右、z が前）をワールド方向へ回す。
        /// カメラが真下・真上を向いて forward の水平成分が消えるときは、
        /// up を平面化した向きを前方の代わりに使う。
        /// </summary>
        protected override Vector3 ResolveWorldDirection(Vector3 stateDirection)
        {
            if (_cameraTransform == null || stateDirection == Vector3.zero) return stateDirection;

            var forward = _cameraTransform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = _cameraTransform.up;
                forward.y = 0f;
            }

            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);

            return right * stateDirection.x + forward * stateDirection.z;
        }

        protected override void OnLookInputChanged(Vector2 lookInput)
        {
            if (transform == null || _panTilt == null) return;
            if (lookInput == Vector2.zero) return;

            _yaw = Mathf.Repeat(_yaw + lookInput.x * _degreesPerInput, 360f);
            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);

            // 入力の上方向へ視点を向ける = カメラの Tilt は負方向へ回る。
            // TiltAxis.Value は MutateCameraState 内でクランプされない為、ここで TiltAxis.Range に収める
            _panTilt.TiltAxis.Value = _panTilt.TiltAxis.ClampValue(_panTilt.TiltAxis.Value - lookInput.y * _degreesPerInput);
        }
    }
}
