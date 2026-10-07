using Unity.Cinemachine;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// PC / スマホ用の視点（上下・左右方向）反映。
    /// Cinemachine カメラの Pan 軸と Tilt 軸を直接書き換え、可動域は CinemachinePanTilt の各軸の Range（Inspector）で管理する。
    /// 体（補間を有効にした Rigidbody）は回さず、左右の向きもカメラ側で持つ。
    /// 視点入力はそのフレーム分の移動量なので、経過時間で割らずに届いたその場で回転へ加算する。
    /// Look 入力は Pointer/delta を使い、カーソルが画面外へ出るとデルタが得られないので、有効化中はカーソルを中央にロックして隠す。
    /// </summary>
    public sealed class StandardPlayerCameraAdapter : PlayerCameraAdapterBase
    {
        [SerializeField]
        [Tooltip("左右方向（Pan）と上下方向（Tilt）の回転を反映するコンポーネント。")]
        private CinemachinePanTilt _panTilt;

        [SerializeField, Min(0f)]
        [Tooltip("感度倍率 1.0 のときの、入力 1 単位あたりの回転角（度）")]
        private float _degreesPerInput = 0.1f;

#if UNITY_EDITOR
        /// <summary> Alt キーでカーソルのロックを外している最中か </summary>
        private bool _isCursorReleased;
#endif

        public override void Initialize()
        {
            base.Initialize();

            if (_panTilt == null)
            {
                UsefulLogger.LogError("CinemachinePanTilt が設定されていません。", this);
            }

            SetCursorLocked(true);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            SetCursorLocked(false);
        }

        protected override void OnLookInputChanged(Vector2 lookInput)
        {
            if (_panTilt == null) return;

            // 各軸の Value は MutateCameraState 内でクランプされない為、ここで Range に収める（Pan は Wrap で一周させる）
            if (lookInput.x != 0f)
            {
                _panTilt.PanAxis.Value = _panTilt.PanAxis.ClampValue(_panTilt.PanAxis.Value + lookInput.x * _degreesPerInput);
            }

            // 入力の上方向へ視点を向ける = カメラの Tilt は負方向へ回る
            if (lookInput.y != 0f)
            {
                _panTilt.TiltAxis.Value = _panTilt.TiltAxis.ClampValue(_panTilt.TiltAxis.Value - lookInput.y * _degreesPerInput);
            }
        }

        /// <summary>
        /// カーソルを画面中央にロックして隠すか、ロックを外して表示する。
        /// </summary>
        /// <param name="locked">ロックして隠すなら true</param>
        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Alt キーを押している間はカーソルのロックを外して表示し、離したらロックして隠す。
        /// エディタで Esc キーによりロックを外す操作を妨げないよう、Alt キーの状態が変わったときだけカーソルを書き換える。
        /// </summary>
        private void Update()
        {
            if (!Initialized) return;

            var keyboard = Keyboard.current;
            var isReleased = keyboard != null && keyboard.altKey.isPressed;
            if (isReleased == _isCursorReleased) return;

            _isCursorReleased = isReleased;
            SetCursorLocked(!isReleased);
        }
#endif
    }
}
