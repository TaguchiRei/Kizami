using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// タッチ領域の UI 上で始まったドラッグの移動量を、外部入力スロットへ書き込む入力ソース。
    /// スマホの視点操作用。
    /// 書き込んだ値は仮想デバイスを経由して、スロットをバインドした InputAction として発火する。
    ///
    /// 仮想デバイスは次に書き込むまで値を保持する為、指が止まっているフレームと指を離した時は
    /// ゼロを書き込む。書き込まないと直前の移動量が残り続け、視点が回り続ける。
    /// </summary>
    public sealed class TouchLookInputSource : InitializableMonoBehaviour
    {
        [SerializeField] private GraphicRaycaster _rayCaster;

        [SerializeField]
        [Tooltip("タッチを受け付ける UI に付いているタグ。ここに当たった時だけ入力として扱う。")]
        private string _touchAreaTag = "TouchArea";

        private IInputState _inputState;
        private IInputController _inputController;
        private Enum _map;
        private Enum _slot;

        private PointerEventData _eventData;
        private readonly List<RaycastResult> _raycastResults = new();

        /// <summary> EnhancedTouch を有効にしたか。OnDestroy で無効化を対にする為に持つ </summary>
        private bool _isTouchEnabled;

        private bool _isTracking;
        private int _trackedTouchId = -1;
        private Vector2 _lastPosition;

        /// <summary> 直前に書き込んだ値がゼロ以外か。ゼロを毎フレーム書き込まない為に持つ </summary>
        private bool _hasWrittenNonZero;

        /// <summary>
        /// 入力を流してよいかの判定に使う読み取り面と、外部入力の書き込み先を渡す。
        /// Initialize より前に呼ぶこと。
        /// </summary>
        /// <param name="inputState">入力の読み取り面</param>
        /// <param name="inputController">入力の操作面</param>
        public void SetInput(IInputState inputState, IInputController inputController)
        {
            _inputState = inputState;
            _inputController = inputController;
        }

        /// <summary>
        /// 書き込みの可否を判定する ActionMap と、書き込み先の外部入力スロットを指定する。
        /// Initialize より前に呼ぶこと。
        /// </summary>
        /// <param name="map">書き込みの可否を判定する ActionMap。スロットをバインドした Action が属するもの</param>
        /// <param name="slot">書き込み先の外部入力スロット</param>
        public void Bind(Enum map, Enum slot)
        {
            _map = map;
            _slot = slot;
        }

        public override void Initialize()
        {
            base.Initialize();

            if (_inputState == null || _inputController == null || _map == null || _slot == null)
            {
                UsefulLogger.LogError(
                    "InputState / InputController / Bind が設定されていません。" +
                    "Initialize() より前に SetInput / Bind を呼んでください。", this);
                return;
            }

            if (_rayCaster == null)
            {
                UsefulLogger.LogError("GraphicRaycaster が設定されていない為、タッチ範囲を判定できません。", this);
                return;
            }

            _eventData = new PointerEventData(EventSystem.current);

            EnhancedTouchSupport.Enable();
#if UNITY_EDITOR
            // エディタ上でマウスクリックをタッチとして扱うシミュレーションを有効化する
            TouchSimulation.Enable();
#endif
            _isTouchEnabled = true;
        }

        private void OnDestroy()
        {
            if (!_isTouchEnabled) return;

            StopTracking();

            EnhancedTouchSupport.Disable();
#if UNITY_EDITOR
            TouchSimulation.Disable();
#endif
            _isTouchEnabled = false;
        }

        private void Update()
        {
            if (!_isTouchEnabled) return;

            if (!_inputState.InputEnabled || !_inputState.IsActionMapActive(_map))
            {
                StopTracking();
                return;
            }

            if (_isTracking)
            {
                UpdateTracking();
                return;
            }

            TryBeginTracking();
        }

        /// <summary>
        /// 追跡中の指の移動量を書き込む。指が離れていれば追跡を終える。
        /// </summary>
        private void UpdateTracking()
        {
            if (!TryFindTrackedTouch(out var touch) || touch.ended)
            {
                StopTracking();
                return;
            }

            var position = touch.screenPosition;
            var delta = position - _lastPosition;
            _lastPosition = position;
            Write(delta);
        }

        /// <summary>
        /// タッチ領域内で始まった指があれば、その指の追跡を始める。
        /// </summary>
        private void TryBeginTracking()
        {
            foreach (var touch in Touch.activeTouches)
            {
                if (!touch.began) continue;

                var position = touch.screenPosition;
                if (!IsInsideTouchArea(position)) continue;

                _trackedTouchId = touch.touchId;
                _isTracking = true;
                _lastPosition = position;
                return;
            }
        }

        /// <summary>
        /// 追跡を終え、ゼロを書き込む。
        /// </summary>
        private void StopTracking()
        {
            _isTracking = false;
            _trackedTouchId = -1;
            Write(Vector2.zero);
        }

        /// <summary>
        /// 追跡中の指を探す。
        /// </summary>
        /// <param name="trackedTouch">見つかった指</param>
        /// <returns>見つかったか</returns>
        private bool TryFindTrackedTouch(out Touch trackedTouch)
        {
            foreach (var touch in Touch.activeTouches)
            {
                if (touch.touchId != _trackedTouchId) continue;

                trackedTouch = touch;
                return true;
            }

            trackedTouch = default;
            return false;
        }

        /// <summary>
        /// 外部入力スロットへ値を書き込む。直前もゼロだった場合のゼロは書き込まない。
        /// </summary>
        /// <param name="value">書き込む値</param>
        private void Write(Vector2 value)
        {
            bool isNonZero = value != Vector2.zero;

            if (!isNonZero && !_hasWrittenNonZero) return;

            _hasWrittenNonZero = isNonZero;
            _inputController.WriteExternalInput(_slot, value);
        }

        /// <summary>
        /// 入力範囲内にあるか、ボタンなどと被っていないかを調べる。
        /// </summary>
        /// <param name="screenPosition">調べるスクリーン座標</param>
        private bool IsInsideTouchArea(Vector2 screenPosition)
        {
            _eventData.position = screenPosition;
            _raycastResults.Clear();
            _rayCaster.Raycast(_eventData, _raycastResults);

            if (_raycastResults.Count == 0) return false;

            return _raycastResults[0].gameObject != null && _raycastResults[0].gameObject.CompareTag(_touchAreaTag);
        }
    }
}
