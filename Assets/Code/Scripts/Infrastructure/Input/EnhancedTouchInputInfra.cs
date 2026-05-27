using System;
using UnityEngine.InputSystem.EnhancedTouch;
using UsefulTools.Application.Runtime.Input;
using UsefulTools.Domain.Runtime;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

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
        }

        public void Update()
        {
            foreach (var touch in Touch.activeTouches)
            {
                var inputData = new TouchInputData(touch.finger.index, touch.screenPosition);

                switch (touch.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        OnTouchBegan?.Invoke(inputData);
                        break;
                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        OnTouchMoved?.Invoke(inputData);
                        break;
                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        OnTouchEnded?.Invoke(touch.finger.index);
                        break;
                }
            }
        }
    }
}