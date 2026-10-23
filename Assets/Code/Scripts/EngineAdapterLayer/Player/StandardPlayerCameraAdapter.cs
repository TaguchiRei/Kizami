using Kizami.BlackBoard;
using Unity.Cinemachine;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
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
    /// 壁走り中は、カメラの上方向が壁から離れる側へ傾くよう、カメラのロール（Lens.Dutch）を傾ける。入るときと出るときは、実時間で滑らかに補間する。
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

        [SerializeField]
        [Tooltip("ロールをかける Cinemachine カメラ。未設定なら、Pan 軸と Tilt 軸を持つコンポーネントと同じ GameObject のものを使う")]
        private CinemachineCamera _cinemachineCamera;

        [SerializeField, Min(0f)]
        [Tooltip("壁走り中にカメラを傾ける角度（度）。壁から離れる側へ傾ける。0 で傾けない")]
        private float _wallRunRollAngle = 5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("壁走りに入るときと出るときに、傾きが目標へ届くまでの時間（秒）")]
        private float _wallRunRollDuration = 0.15f;

        private IPlayerMovementState _movementState;
        private IPlayerContactState _contactState;

        /// <summary> 今のロールの角度（度）。Lens.Dutch へ書く値 </summary>
        private float _roll;

        /// <summary> 傾ける向きの符号。壁の法線が視点の右を向くとき -1、左を向くとき 1。壁走りを終えて戻る間も、直前の向きを使う </summary>
        private float _rollDirection = 1f;

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

            if (_cinemachineCamera == null && _panTilt != null) _cinemachineCamera = _panTilt.GetComponent<CinemachineCamera>();

            SetCursorLocked(true);
        }

        protected override void OnBlackBoardReady(IBlackBoard blackBoard)
        {
            blackBoard.TryGetSceneState<PlayerBoard, IPlayerMovementState>(out _movementState, this);
            blackBoard.TryGetSceneState<PlayerBoard, IPlayerContactState>(out _contactState, this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            SetCursorLocked(false);
        }

        public override void FaceYaw(float yaw)
        {
            if (_panTilt == null) return;

            _panTilt.PanAxis.Value = _panTilt.PanAxis.ClampValue(yaw);
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

        private void LateUpdate()
        {
            if (!Initialized || _cinemachineCamera == null || _movementState == null || _contactState == null) return;

            var isWallRunning = _movementState.Mode == PlayerMoveMode.WallRunning;
            if (isWallRunning) _rollDirection = GetRollDirection();

            var target = isWallRunning ? _rollDirection * _wallRunRollAngle : 0f;
            var maxDelta = _wallRunRollAngle / _wallRunRollDuration * Time.unscaledDeltaTime;
            var roll = Mathf.MoveTowards(_roll, target, maxDelta);
            if (Mathf.Approximately(roll, _roll)) return;

            _roll = roll;
            var lens = _cinemachineCamera.Lens;
            lens.Dutch = _roll;
            _cinemachineCamera.Lens = lens;
        }

        /// <summary>
        /// 壁の法線（壁から離れる向き）が視点の右を向くなら -1、左を向くなら 1 を返す。カメラの上方向を法線の側へ傾けるロールの符号。
        /// </summary>
        private float GetRollDirection()
        {
            var cameraMain = Camera.main;
            if (cameraMain == null) return _rollDirection;

            var side = Vector3.Dot(_contactState.WallNormal, cameraMain.transform.right);
            return Mathf.Approximately(side, 0f) ? _rollDirection : -Mathf.Sign(side);
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
