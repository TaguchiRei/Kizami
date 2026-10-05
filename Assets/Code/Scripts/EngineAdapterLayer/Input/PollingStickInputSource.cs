using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// スティック入力を毎フレーム読み出して外部入力スロットへ書き込む、VR コントローラ用の入力ソース。
    /// 書き込んだ値は仮想デバイスを経由して、スロットをバインドした InputAction として発火する。
    /// XR デバイスのスティックは入力を継続していても started / canceled が繰り返し発火するので、
    /// InputAction のコールバックは購読せず、ReadValue の現在値だけを使う。
    /// デッドゾーンは InputActionAsset 側の StickDeadzone プロセッサーが適用し、範囲内の入力はゼロとして読める。
    /// </summary>
    public sealed class PollingStickInputSource : InitializableMonoBehaviour
    {
        private IInputState _inputState;
        private IInputController _inputController;
        private Enum _sourceMap;
        private Enum _sourceAction;
        private Enum _destinationMap;
        private Enum _destinationSlot;

        /// <summary> 直前のフレームでゼロ以外の値を書き込んでいたか。ゼロを 1 度だけ書き込む為に持つ </summary>
        private bool _isInputActive;

        /// <summary>
        /// 入力の読み出し元と外部入力の書き込み先、どの (map, action) を読み出してどの外部入力スロットへ書き込むかを渡す。
        /// </summary>
        /// <param name="blackBoard">入力の読み取り面の取得元</param>
        /// <param name="inputController">入力の操作面</param>
        /// <param name="sourceMap">読み出し元の ActionMap</param>
        /// <param name="sourceAction">読み出し元の Action</param>
        /// <param name="destinationMap">書き込みの可否を判定する ActionMap。スロットをバインドした Action が属するもの</param>
        /// <param name="destinationSlot">書き込み先の外部入力スロット</param>
        public void Initialize(IBlackBoard blackBoard, IInputController inputController, Enum sourceMap,
            Enum sourceAction, Enum destinationMap, Enum destinationSlot)
        {
            if (!blackBoard.TryGetGameState<InputBoard, IInputState>(out _inputState, this)) return;

            _inputController = inputController;
            _sourceMap = sourceMap;
            _sourceAction = sourceAction;
            _destinationMap = destinationMap;
            _destinationSlot = destinationSlot;

            Initialize();
        }

        private void OnDestroy()
        {
            ReleaseIfActive();
        }

        private void Update()
        {
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

            if (value == Vector2.zero)
            {
                ReleaseIfActive();
                return;
            }

            _isInputActive = true;
            _inputController.WriteExternalInput(_destinationSlot, value);
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
