using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UsefulTools.Application.Runtime.Input;
using UsefulTools.Domain.Runtime;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.InputSystem.UI;

namespace UsefulTools.Infrastructure.Runtime.Input
{
    /// <summary>
    /// EnhancedTouchSupportを利用してタッチ入力を配信するインフラ実装
    /// </summary>
    public sealed class EnhancedTouchInputInfra : ITouchAreaInputInfra, IUpdateRule
    {
        public event Action<TouchInputData> OnTouchBegan;
        public event Action<TouchInputData> OnTouchMoved;
        public event Action<int> OnTouchEnded;

        public RuleState State { get; private set; } = RuleState.Playing;
        public event Action<RuleState> OnGameEndAction;

        public void StartGame()
        {
            if (!EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Enable();
            }

#if UNITY_EDITOR
            // Unity上ではクリックをタッチとしてシミュレーションを有効化
            if (!TouchSimulation.instance?.enabled ?? true)
            {
                TouchSimulation.Enable();
            }
#endif
            State = RuleState.Playing;
        }

        public void Pause() { }

        public void Resume() { }

        public void Stop()
        {
            if (EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Disable();
            }
#if UNITY_EDITOR
            TouchSimulation.Disable();
#endif
        }

        public void Update()
        {
            foreach (var touch in Touch.activeTouches)
            {
                // touchIdを使用して追跡の一貫性を確保
                var inputData = new TouchInputData(touch.touchId, touch.screenPosition);

                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    OnTouchBegan?.Invoke(inputData);
                }
                else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved || touch.phase == UnityEngine.InputSystem.TouchPhase.Stationary)
                {
                    OnTouchMoved?.Invoke(inputData);
                }
                else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    OnTouchEnded?.Invoke(touch.touchId);
                }
            }
        }
    }
}
