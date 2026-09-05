using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// スティック入力を毎フレーム読み出して、別の (map, action) チャンネルへ流す入力ソース。
    /// VR コントローラのスティック用。
    ///
    /// InputAction の started / canceled は一切購読しない。XR デバイスのスティックでは
    /// 入力を継続していても started と canceled が繰り返し発火する為、コールバック経由では
    /// 入力の継続を正しく追えない。現在値の読み出し (ReadValue) だけを真とし、
    /// phase はこのクラスが値の変化から組み立てる。
    ///
    /// 読み出し元の ActionMap と、流し込み先の ActionMap は別で構わない
    /// (VRControllers を読んで Player へ流す)。発火の可否は流し込み先の ActionMap が
    /// 有効かどうかで判定する為、アウトゲーム / インゲームの ActionMap 切り替えに追従する。
    /// </summary>
    public sealed class PollingStickInputSource : InitializableMonoBehaviour, IExternalInputSource<Vector2>
    {
        [SerializeField, Range(0f, 0.9f)]
        [Tooltip("この大きさ以下の入力は無入力として扱う。")]
        private float _deadZone = 0.15f;

        private IInputState _inputState;
        private IInputController _inputController;
        private Enum _sourceMap;
        private Enum _sourceAction;
        private Enum _destinationMap;
        private Enum _destinationAction;
        private bool _ignoreVertical;

        private Action<InputContext<Vector2>> _onInput;
        private IDisposable _registration;

        /// <summary> 直前のフレームで入力を流していたか。Canceled を 1 度だけ流す為に持つ </summary>
        private bool _isInputActive;

        /// <summary>
        /// 入力の読み出し元と、入力ソースの登録先を渡す。Initialize より前に呼ぶこと。
        /// </summary>
        /// <param name="inputState">入力の読み取り面</param>
        /// <param name="inputController">入力の操作面</param>
        public void SetInput(IInputState inputState, IInputController inputController)
        {
            _inputState = inputState;
            _inputController = inputController;
        }

        /// <summary>
        /// どの (map, action) を読み出し、どの (map, action) として流すかを指定する。
        /// Initialize より前に呼ぶこと。
        /// </summary>
        /// <param name="sourceMap">読み出し元の ActionMap</param>
        /// <param name="sourceAction">読み出し元の Action</param>
        /// <param name="destinationMap">流し込み先の ActionMap</param>
        /// <param name="destinationAction">流し込み先の Action</param>
        /// <param name="ignoreVertical">縦方向の入力を捨てるか。VR の視点操作 (左右のみ) では true</param>
        public void Bind(Enum sourceMap, Enum sourceAction, Enum destinationMap, Enum destinationAction,
            bool ignoreVertical = false)
        {
            _sourceMap = sourceMap;
            _sourceAction = sourceAction;
            _destinationMap = destinationMap;
            _destinationAction = destinationAction;
            _ignoreVertical = ignoreVertical;
        }

        public override void Initialize()
        {
            base.Initialize();

            if (_inputState == null || _inputController == null ||
                _sourceMap == null || _sourceAction == null ||
                _destinationMap == null || _destinationAction == null)
            {
                UsefulLogger.LogError(
                    "InputState / InputController / Bind が設定されていません。" +
                    "Initialize() より前に SetInput / Bind を呼んでください。", this);
                return;
            }

            _registration = _inputController.RegisterExternalInputSource(
                _destinationMap, _destinationAction, this);
        }

        private void OnDestroy()
        {
            _registration?.Dispose();
        }

        public void RegisterAction(Action<InputContext<Vector2>> handler) => _onInput += handler;
        public void UnRegisterAction(Action<InputContext<Vector2>> handler) => _onInput -= handler;

        private void Update()
        {
            if (_inputState == null || _inputController == null ||
                _sourceMap == null || _destinationMap == null) return;

            if (!_inputState.InputEnabled || !_inputState.IsActionMapActive(_destinationMap))
            {
                RaiseCancelIfActive();
                return;
            }

            // 読み出し元の ActionMap が無効だと ReadValue が値を返さない。
            // 流し込み先の切り替えに引きずられて落ちる為、有効な間は毎フレーム張り直す
            if (!_inputState.IsActionMapActive(_sourceMap))
            {
                _inputController.EnableActionMap(_sourceMap);
            }

            var value = _inputState.ReadValue<Vector2>(_sourceMap, _sourceAction).Value;

            if (_ignoreVertical) value.y = 0f;

            if (value.magnitude <= _deadZone)
            {
                RaiseCancelIfActive();
                return;
            }

            // 初回だけ Started、以降は Performed。値そのものはどちらでも同じものを載せる
            var phase = _isInputActive ? InputPhase.Performed : InputPhase.Started;
            _isInputActive = true;
            _onInput?.Invoke(new InputContext<Vector2>(phase, value));
        }

        /// <summary>
        /// 入力を流していた場合に限り、打ち切りを 1 度だけ通知する。
        /// </summary>
        private void RaiseCancelIfActive()
        {
            if (!_isInputActive) return;

            _isInputActive = false;
            _onInput?.Invoke(new InputContext<Vector2>(InputPhase.Canceled, Vector2.zero));
        }
    }
}
