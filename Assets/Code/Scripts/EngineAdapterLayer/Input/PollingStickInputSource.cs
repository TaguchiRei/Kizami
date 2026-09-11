using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// スティック入力を毎フレーム読み出して、外部入力スロットへ書き込む入力ソース。
    /// VR コントローラのスティック用。
    /// 書き込んだ値は仮想デバイスを経由して、スロットをバインドした InputAction として発火する。
    ///
    /// InputAction の started / canceled は一切購読しない。XR デバイスのスティックでは
    /// 入力を継続していても started と canceled が繰り返し発火する為、コールバック経由では
    /// 入力の継続を正しく追えない。現在値の読み出し (ReadValue) だけを真とする。
    ///
    /// 書き込みの可否は流し込み先の ActionMap が有効かどうかで判定する為、
    /// アウトゲーム / インゲームの ActionMap 切り替えに追従する。
    /// </summary>
    public sealed class PollingStickInputSource : InitializableMonoBehaviour
    {
        [SerializeField, Range(0f, 0.9f)]
        [Tooltip("この大きさ以下の入力は無入力として扱う。")]
        private float _deadZone = 0.15f;

        private IInputState _inputState;
        private IInputController _inputController;
        private Enum _sourceMap;
        private Enum _sourceAction;
        private Enum _destinationMap;
        private Enum _destinationSlot;
        private bool _ignoreVertical;

        /// <summary> 直前のフレームでゼロ以外の値を書き込んでいたか。ゼロを 1 度だけ書き込む為に持つ </summary>
        private bool _isInputActive;

        /// <summary>
        /// 入力の読み出し元と、外部入力の書き込み先を渡す。Initialize より前に呼ぶこと。
        /// </summary>
        /// <param name="inputState">入力の読み取り面</param>
        /// <param name="inputController">入力の操作面</param>
        public void SetInput(IInputState inputState, IInputController inputController)
        {
            _inputState = inputState;
            _inputController = inputController;
        }

        /// <summary>
        /// どの (map, action) を読み出し、どの外部入力スロットへ書き込むかを指定する。
        /// Initialize より前に呼ぶこと。
        /// </summary>
        /// <param name="sourceMap">読み出し元の ActionMap</param>
        /// <param name="sourceAction">読み出し元の Action</param>
        /// <param name="destinationMap">書き込みの可否を判定する ActionMap。スロットをバインドした Action が属するもの</param>
        /// <param name="destinationSlot">書き込み先の外部入力スロット</param>
        /// <param name="ignoreVertical">縦方向の入力を捨てるか。VR の視点操作 (左右のみ) では true</param>
        public void Bind(Enum sourceMap, Enum sourceAction, Enum destinationMap, Enum destinationSlot,
            bool ignoreVertical = false)
        {
            _sourceMap = sourceMap;
            _sourceAction = sourceAction;
            _destinationMap = destinationMap;
            _destinationSlot = destinationSlot;
            _ignoreVertical = ignoreVertical;
        }

        public override void Initialize()
        {
            base.Initialize();

            if (!IsConfigured())
            {
                UsefulLogger.LogError(
                    "InputState / InputController / Bind が設定されていません。" +
                    "Initialize() より前に SetInput / Bind を呼んでください。", this);
            }
        }

        private void OnDestroy()
        {
            ReleaseIfActive();
        }

        private void Update()
        {
            if (!IsConfigured()) return;

            if (!_inputState.InputEnabled || !_inputState.IsActionMapActive(_destinationMap))
            {
                ReleaseIfActive();
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
                ReleaseIfActive();
                return;
            }

            _isInputActive = true;
            _inputController.WriteExternalInput(_destinationSlot, value);
        }

        /// <summary>
        /// SetInput / Bind の内容が全て揃っているか。
        /// </summary>
        private bool IsConfigured()
        {
            return _inputState != null && _inputController != null &&
                   _sourceMap != null && _sourceAction != null &&
                   _destinationMap != null && _destinationSlot != null;
        }

        /// <summary>
        /// ゼロ以外の値を書き込んでいた場合に限り、ゼロを 1 度だけ書き込む。
        /// </summary>
        private void ReleaseIfActive()
        {
            if (!_isInputActive) return;

            _isInputActive = false;
            _inputController?.WriteExternalInput(_destinationSlot, Vector2.zero);
        }
    }
}
