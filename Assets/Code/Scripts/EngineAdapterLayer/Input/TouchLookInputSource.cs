using System;
using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.EventSystems;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// タッチ領域の UI 上で始まったドラッグの移動量を外部入力スロットへ書き込む、スマホの視点操作用の入力ソース。
    /// Raycast Target を有効にした Graphic（透明な Image など）と同じ GameObject に付ける。
    /// どの UI の上でドラッグが始まったかの判定と、指ごとの追跡は EventSystem のドラッグ通知に任せる。
    /// </summary>
    /// <remarks>
    /// 書き込んだ値は仮想デバイスを経由して、スロットをバインドした InputAction として発火する。
    /// 仮想デバイスは次に書き込むまで値を保持するので、指が止まっているフレームと指を離したときはゼロを書き込む。
    /// EventSystem は Update でドラッグを通知するので、そのフレームの移動量の合計は LateUpdate で書き込む。
    /// </remarks>
    public sealed class TouchLookInputSource : InitializableMonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private IInputState _inputState;
        private IInputController _inputController;
        private Enum _map;
        private Enum _slot;

        /// <summary> 追跡中の指のポインター ID。追跡していないときは null </summary>
        private int? _trackedPointerId;

        /// <summary> このフレームに届いたドラッグの移動量の合計（スクリーン座標） </summary>
        private Vector2 _frameDelta;

        /// <summary> 直前に書き込んだ値がゼロ以外か。ゼロの書き込みを 1 回に抑えるために持つ </summary>
        private bool _hasWrittenInput;

        /// <summary>
        /// 入力を流してよいかの判定に使う読み取り面と外部入力の書き込み先、書き込みの可否を判定する ActionMap と書き込み先のスロットを渡す。
        /// </summary>
        /// <param name="blackBoard">入力の読み取り面の取得元</param>
        /// <param name="inputController">入力の操作面</param>
        /// <param name="map">書き込みの可否を判定する ActionMap。スロットをバインドした Action が属するもの</param>
        /// <param name="slot">書き込み先の外部入力スロット</param>
        public void Initialize(IBlackBoard blackBoard, IInputController inputController, Enum map, Enum slot)
        {
            if (!blackBoard.TryGetGameState<InputBoard, IInputState>(out _inputState, this)) return;

            _inputController = inputController;
            _map = map;
            _slot = slot;

            Initialize();
        }

        private void LateUpdate()
        {
            if (!_inputState.InputEnabled || !_inputState.IsActionMapActive(_map))
            {
                _trackedPointerId = null;
                _frameDelta = Vector2.zero;
                Write(Vector2.zero);
                return;
            }

            Write(_frameDelta);
            _frameDelta = Vector2.zero;
        }

        private void OnDestroy()
        {
            Write(Vector2.zero);
        }

        /// <summary>
        /// 外部入力スロットへ値を書き込む。ゼロが続くときは最初の 1 回だけ書き込む。
        /// </summary>
        /// <param name="value">書き込む値</param>
        private void Write(Vector2 value)
        {
            var hasInput = value != Vector2.zero;
            if (!hasInput && !_hasWrittenInput) return;

            _hasWrittenInput = hasInput;
            _inputController.WriteExternalInput(_slot, value);
        }

        /// <summary>
        /// ドラッグとみなすまでの移動量の閾値を使わない。閾値を超えるまでの移動量も視点操作に含める為。
        /// </summary>
        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        /// <summary>
        /// 追跡中の指がなければ、この指の追跡を始める。
        /// </summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!Initialized || _trackedPointerId.HasValue) return;

            _trackedPointerId = eventData.pointerId;
        }

        /// <summary>
        /// 追跡中の指の移動量を、このフレームの合計に加える。
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _trackedPointerId) return;

            _frameDelta += eventData.delta;
        }

        /// <summary>
        /// 追跡中の指が離れたら追跡を終える。
        /// </summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _trackedPointerId) return;

            _trackedPointerId = null;
        }
    }
}
