using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UsefulTools.AutoGenerate;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace UsefulTools.Infrastructure.Runtime.Input
{
    public class InputDispatcher : InitializableMonoBehaviour, IInputDispatcher
    {
        [SerializeField] private InputActionAsset _actionAsset;

        /// <summary>
        /// ReadValue系登録
        /// </summary>
        private readonly Dictionary<Delegate, Action> _registeredReadActions = new();

        /// <summary>
        /// 外部公開される論理入力イベント
        /// Key: (ActionMap, ActionName, typeof(T), Phase)
        /// </summary>
        private readonly Dictionary<InputEventKey, Delegate> _logicalInputEvents = new();

        /// <summary>
        /// 外部InputSource登録解除用
        /// </summary>
        private readonly Dictionary<object, Delegate> _externalInputCallbacks = new();

        /// <summary>
        /// Polling入力用
        /// </summary>
        private readonly Dictionary<InputAction, bool> _previousInputStates = new();

        /// <summary>
        /// Unity InputSystem のコールバック（値なし・フェーズのみ）の登録先
        /// Key: (ActionMap, ActionName, Phase)
        /// </summary>
        private readonly Dictionary<PhaseOnlyKey, Action<InputActionPhase>> _phaseOnlyEvents = new();

        public override void Initialize()
        {
            base.Initialize();

            _actionAsset.Enable();

            RegisterUnityInputCallbacks();
        }

        private void Update()
        {
            foreach (var updateAction in _registeredReadActions.Values)
            {
                updateAction();
            }
        }

        private void OnDestroy()
        {
            _registeredReadActions.Clear();
            _logicalInputEvents.Clear();
            _externalInputCallbacks.Clear();
            _previousInputStates.Clear();
            _phaseOnlyEvents.Clear();

            _actionAsset.Disable();
        }

        #region ReadValue

        public InputContext<T> ReadValue<T, TAction>(ActionMaps actionMap, TAction actionName)
            where T : unmanaged where TAction : Enum
        {
            var action = GetAction(actionMap.ToString(), actionName.ToString());

            if (action == null)
            {
                Debug.LogWarning($"[InputDispatcher] {actionMap}.{actionName} は見つかりませんでした。");

                return new InputContext<T>(InputActionPhase.Disabled, default);
            }

            return new InputContext<T>(action.phase, action.ReadValue<T>());
        }

        public void RegistrationReadValue<T, TAction>(ActionMaps actionMap, TAction actionName, Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            var inputAction = GetAction(actionMap.ToString(), actionName.ToString());

            if (inputAction == null)
            {
                Debug.LogWarning($"[InputDispatcher] {actionMap}.{actionName} は見つかりませんでした。");

                return;
            }

            if (isRegister)
            {
                if (_registeredReadActions.ContainsKey(action)) return;

                void UpdateAction()
                {
                    T value = inputAction.ReadValue<T>();

                    bool currentActive =
                        !EqualityComparer<T>.Default.Equals(value, default);

                    bool previousActive =
                        _previousInputStates.GetValueOrDefault(inputAction);

                    InputActionPhase phase;

                    if (!previousActive && currentActive)
                    {
                        phase = InputActionPhase.Started;
                    }
                    else if (previousActive && currentActive)
                    {
                        phase = InputActionPhase.Performed;
                    }
                    else if (previousActive && !currentActive)
                    {
                        phase = InputActionPhase.Canceled;
                    }
                    else
                    {
                        phase = InputActionPhase.Waiting;
                    }

                    _previousInputStates[inputAction] = currentActive;

                    action?.Invoke(new InputContext<T>(phase, value));
                }

                _registeredReadActions.Add(action, UpdateAction);
            }
            else
            {
                _registeredReadActions.Remove(action);
            }
        }

        #endregion

        #region Registration

        public void RegistrationStarted<T, TAction>(ActionMaps actionMap, TAction actionName, Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            RegistrationLogical(actionMap, actionName, action, isRegister, InputActionPhase.Started);
        }

        public void RegistrationPerformed<T, TAction>(ActionMaps actionMap, TAction actionName, Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            RegistrationLogical(actionMap, actionName, action, isRegister, InputActionPhase.Performed);
        }

        public void RegistrationCancelled<T, TAction>(ActionMaps actionMap, TAction actionName, Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            RegistrationLogical(actionMap, actionName, action, isRegister, InputActionPhase.Canceled);
        }

        public void RegistrationStartCancelled<T, TAction>(ActionMaps actionMap, TAction actionName, Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            RegistrationLogical(actionMap, actionName, action, isRegister, InputActionPhase.Started, InputActionPhase.Canceled);
        }

        public void RegistrationAll<T, TAction>(ActionMaps actionMap, TAction actionName, Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            RegistrationLogical(actionMap, actionName, action, isRegister,
                InputActionPhase.Started, InputActionPhase.Performed, InputActionPhase.Canceled);
        }

        #endregion

        #region ExternalInput

        /// <summary>
        /// 外部入力を論理入力へ紐づける
        /// </summary>
        public void RegisterExternalInput<T, TAction>(ActionMaps actionMap, TAction actionName, IInputSource<T> inputSource)
            where T : unmanaged where TAction : Enum
        {
            var callbackKey = (object)(actionMap, actionName, inputSource);

            if (_externalInputCallbacks.ContainsKey(callbackKey)) return;

            void Callback(InputContext<T> context)
            {
                DispatchLogicalInput(actionMap.ToString(), actionName.ToString(), context.Phase, context.Value);
            }

            inputSource.RegisterAction(Callback);

            _externalInputCallbacks.Add(callbackKey, (Action<InputContext<T>>)Callback);
        }

        public void UnregisterExternalInput<T, TAction>(ActionMaps actionMap, TAction actionName, IInputSource<T> inputSource)
            where T : unmanaged where TAction : Enum
        {
            var callbackKey =
                (object)(actionMap, actionName, inputSource);

            if (!_externalInputCallbacks.TryGetValue(callbackKey, out var callback)) return;

            inputSource.UnRegisterAction((Action<InputContext<T>>)callback);

            _externalInputCallbacks.Remove(callbackKey);
        }

        #endregion

        #region ActionMap

        public void SwitchActionMap(ActionMaps actionMap)
        {
            foreach (var map in _actionAsset.actionMaps)
            {
                map.Disable();
            }

            FindMap(actionMap)?.Enable();
        }

        public void EnableActionMap(ActionMaps actionMap)
        {
            FindMap(actionMap)?.Enable();
        }

        public void DisableActionMap(ActionMaps actionMap)
        {
            FindMap(actionMap)?.Disable();
        }

        public ActionMaps[] GetActiveActionMap()
        {
            var activeMaps = new List<ActionMaps>();

            foreach (var map in _actionAsset.actionMaps)
            {
                if (!map.enabled) continue;

                if (Enum.TryParse(map.name, out ActionMaps parsed))
                {
                    activeMaps.Add(parsed);
                }
            }

            return activeMaps.ToArray();
        }

        public void EnableInput()
        {
            _actionAsset.Enable();
        }

        public void DisableInput()
        {
            _actionAsset.Disable();
        }

        #endregion

        #region Internal

        private void RegisterUnityInputCallbacks()
        {
            foreach (var map in _actionAsset.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    // ローカル変数にコピーしてクロージャのキャプチャを安定させる
                    string mapName = map.name;
                    string actionName = action.name;

                    action.started += context =>
                        DispatchPhaseOnly(mapName, actionName, context.phase);

                    action.performed += context =>
                        DispatchPhaseOnly(mapName, actionName, context.phase);

                    action.canceled += context =>
                        DispatchPhaseOnly(mapName, actionName, context.phase);
                }
            }
        }

        /// <summary>
        /// 値型情報なし・フェーズのみで購読しているリスナーへ通知する
        /// （RegisterUnityInputCallbacks からのみ呼ばれる）
        /// </summary>
        private void DispatchPhaseOnly(string mapName, string actionName, InputActionPhase phase)
        {
            var key = new PhaseOnlyKey(mapName, actionName, phase);

            if (_phaseOnlyEvents.TryGetValue(key, out var handler))
            {
                handler?.Invoke(phase);
            }
        }

        private void RegistrationLogical<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            Action<InputContext<T>> action,
            bool isRegister,
            params InputActionPhase[] phases)
            where T : unmanaged
            where TAction : Enum
        {
            foreach (var phase in phases)
            {
                var key = new InputEventKey(actionMap.ToString(), actionName.ToString(), typeof(T), phase);

                if (isRegister)
                {
                    if (_logicalInputEvents.TryGetValue(key, out var existing))
                    {
                        _logicalInputEvents[key] = Delegate.Combine(existing, action);
                    }
                    else
                    {
                        _logicalInputEvents.Add(key, action);
                    }
                }
                else
                {
                    if (!_logicalInputEvents.TryGetValue(key, out var existing)) continue;

                    var result = Delegate.Remove(existing, action);

                    if (result == null)
                    {
                        _logicalInputEvents.Remove(key);
                    }
                    else
                    {
                        _logicalInputEvents[key] = result;
                    }
                }
            }
        }

        private void DispatchLogicalInput<T>(string actionMap, string actionName, InputActionPhase phase, T value) where T : unmanaged
        {
            var key = new InputEventKey(actionMap, actionName, typeof(T), phase);

            if (!_logicalInputEvents.TryGetValue(key, out var callback)) return;

            ((Action<InputContext<T>>)callback)?.Invoke(new InputContext<T>(phase, value));
        }

        private InputActionMap FindMap(ActionMaps actionMap)
        {
            return _actionAsset.FindActionMap(actionMap.ToString());
        }

        private InputAction GetAction(string actionMap, string actionName)
        {
            return _actionAsset.FindActionMap(actionMap)?.FindAction(actionName);
        }

        #endregion

        private readonly struct InputEventKey : IEquatable<InputEventKey>
        {
            private readonly string _actionMap;
            private readonly string _actionName;
            private readonly Type _valueType;
            private readonly InputActionPhase _phase;

            public InputEventKey(
                string actionMap,
                string actionName,
                Type valueType,
                InputActionPhase phase)
            {
                _actionMap = actionMap;
                _actionName = actionName;
                _valueType = valueType;
                _phase = phase;
            }

            public bool Equals(InputEventKey other)
            {
                return _actionMap == other._actionMap
                    && _actionName == other._actionName
                    && _valueType == other._valueType
                    && _phase == other._phase;
            }

            public override bool Equals(object obj)
            {
                return obj is InputEventKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_actionMap, _actionName, _valueType, _phase);
            }
        }

        private readonly struct PhaseOnlyKey : IEquatable<PhaseOnlyKey>
        {
            private readonly string _actionMap;
            private readonly string _actionName;
            private readonly InputActionPhase _phase;

            public PhaseOnlyKey(string actionMap, string actionName, InputActionPhase phase)
            {
                _actionMap = actionMap;
                _actionName = actionName;
                _phase = phase;
            }

            public bool Equals(PhaseOnlyKey other)
            {
                return _actionMap == other._actionMap
                    && _actionName == other._actionName
                    && _phase == other._phase;
            }

            public override bool Equals(object obj)
            {
                return obj is PhaseOnlyKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_actionMap, _actionName, _phase);
            }
        }
    }
}