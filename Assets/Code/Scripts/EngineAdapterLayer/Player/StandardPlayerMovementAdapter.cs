using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// PC / スマホ用の移動・視点反映。
    /// 視点は水平方向を体に、上下方向をカメラに入れる。体を上下に傾けないのは、
    /// 移動方向が体の向きに従う為。
    ///
    /// 視点入力はマウスや指の移動量として扱う為、届いたその場で回転へ加算する。
    /// 経過時間では割らない（移動量そのものが既にそのフレーム分の量である為）。
    /// </summary>
    public sealed class StandardPlayerMovementAdapter : PlayerMovementAdapterBase
    {
        /// <summary> 上下方向の可動域（度）。真上・真下で像が反転しない範囲に収める </summary>
        private const float PitchLimit = 89f;

        [Header("視点")]
        [SerializeField]
        [Tooltip("上下方向の回転を反映する Transform。カメラ。体の子である必要がある。")]
        private Transform _cameraTransform;

        [SerializeField, Min(0f)]
        [Tooltip("感度倍率 1.0 のときの、入力 1 単位あたりの回転角（度）")]
        private float _degreesPerInput = 0.1f;

        private float _yaw;
        private float _pitch;

        public override void Initialize()
        {
            base.Initialize();

            if (_cameraTransform == null)
            {
                UsefulLogger.LogError("体またはカメラの Transform が設定されていません。", this);
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
            if (transform == null || _cameraTransform == null) return;
            if (lookInput == Vector2.zero) return;

            _yaw = Mathf.Repeat(_yaw + lookInput.x * _degreesPerInput, 360f);

            // 入力の上方向へ視点を向ける = カメラの Pitch は負方向へ回る
            _pitch = Mathf.Clamp(_pitch - lookInput.y * _degreesPerInput, -PitchLimit, PitchLimit);

            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            _cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
