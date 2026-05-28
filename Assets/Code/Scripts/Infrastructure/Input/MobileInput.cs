using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using UsefulTools.AutoGenerate;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UsefulTools.Infrastructure.Runtime.Input;

namespace UsefulTools.Infrastructure.Runtime
{
    public class MobileInput : MonoBehaviour, IInputSource<Vector2>
    {
        [SerializeField] private GraphicRaycaster _rayCaster;
        [SerializeField] private InputDispatcher _inputDispatcher;

        private Action<InputContext<Vector2>> _onInput;

        private PointerEventData _eventData;
        private EventSystem _eventSystem;

        private bool _initialized;
        private bool _isTracking;

        private Vector2 _legacyPosition;
        private int _trackedTouchID = -1;

        private readonly List<RaycastResult> _raycastResults = new();

        public void Initialize()
        {
            _initialized = true;

            _eventSystem = EventSystem.current;
            _eventData = new PointerEventData(_eventSystem);

            EnhancedTouchSupport.Enable();

#if UNITY_EDITOR
            // Unity上ではクリックをタッチとしてシミュレーションを有効化する必要がある
            TouchSimulation.Enable();
#endif

            Debug.Log("MobileInput Initialize");
        }

        private void Awake()
        {
            _inputDispatcher.RegisterExternalInput(ActionMaps.ExternalInput, ExternalActions.MobileInput, this);
        }

        private void Update()
        {
            if (!_initialized) return;

            var touches = Touch.activeTouches;

            // 追跡中の入力を更新
            if (_isTracking)
            {
                Touch? trackingTouch = null;

                foreach (var touch in touches)
                {
                    if (touch.touchId != _trackedTouchID) continue;

                    trackingTouch = touch;
                    break;
                }

                // 指が離れた
                if (!trackingTouch.HasValue || trackingTouch.Value.ended)
                {
                    _isTracking = false;
                    _trackedTouchID = -1;

                    OnTrackedTouchCanceled(_legacyPosition);
                    return;
                }

                Vector2 currentPosition = trackingTouch.Value.screenPosition;

                OnTrackedTouchMoved(currentPosition);

                return;
            }

            // 新規入力検出
            foreach (var touch in touches)
            {
                if (!touch.began) continue;

                Vector2 position = touch.screenPosition;

                if (!IsInsideTouchArea(position)) continue;

                _trackedTouchID = touch.touchId;
                _isTracking = true;

                OnTrackedTouchBegan(position);

                break;
            }
        }

        private void OnDestroy()
        {
            EnhancedTouchSupport.Disable();
            _inputDispatcher.UnregisterExternalInput(ActionMaps.Player, ExternalActions.MobileInput, this);

#if UNITY_EDITOR
            TouchSimulation.Disable();
#endif
        }

        private void OnTrackedTouchBegan(Vector2 screenPos)
        {
            _legacyPosition = screenPos;

            InvokeInput(InputActionPhase.Started, Vector2.zero);
        }

        private void OnTrackedTouchMoved(Vector2 screenPos)
        {
            Vector2 delta = screenPos - _legacyPosition;

            _legacyPosition = screenPos;

            InvokeInput(InputActionPhase.Performed, delta);
        }

        private void OnTrackedTouchCanceled(Vector2 screenPos)
        {
            InvokeInput(InputActionPhase.Canceled, Vector2.zero);
        }

        private void InvokeInput(InputActionPhase phase, Vector2 value)
        {
            _onInput?.Invoke(new InputContext<Vector2>(phase, value));
        }

        /// <summary>
        /// 入力範囲内にあるか、ボタンなどと被っていないかを調べる。
        /// </summary>
        private bool IsInsideTouchArea(Vector2 screenPosition)
        {
            const string TAG_NAME = "TouchArea";

            _eventData.position = screenPosition;

            _raycastResults.Clear();

            _rayCaster.Raycast(_eventData, _raycastResults);

            if (_raycastResults.Count == 0)
                return false;

            return _raycastResults[0].gameObject != null &&
                   _raycastResults[0].gameObject.CompareTag(TAG_NAME);
        }

        public void RegisterAction(Action<InputContext<Vector2>> input)
        {
            Debug.Log("MobileInput RegisterAction");
            _onInput += input;
        }

        public void UnRegisterAction(Action<InputContext<Vector2>> input)
        {
            _onInput -= input;
        }
    }
}