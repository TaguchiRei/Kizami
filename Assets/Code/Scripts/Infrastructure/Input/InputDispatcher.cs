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

        private readonly Dictionary<Delegate, Action> _registeredReadActions = new();
        private readonly Dictionary<InputEventKey, Delegate> _logicalInputEvents = new();
        private readonly Dictionary<object, Delegate> _externalInputCallbacks = new();
        private readonly Dictionary<InputAction, bool> _previousInputStates = new();

        public override void Initialize()
        {
            base.Initialize();

            _actionAsset.Enable();
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

            _actionAsset.Disable();
        }

        #region ReadValue

        public InputContext<T> ReadValue<T, TAction>(ActionMaps actionMap, TAction actionName)
            where T : unmanaged where TAction : Enum
        {
            var action = GetAction(actionMap.ToString(), actionName.ToString());

            if (action == null)
            {
                Debug.LogWarning($"[InputDispatcher] {actionMap}.{actionName} not found.");
                return new InputContext<T>(InputActionPhase.Disabled, default);
            }

            return new InputContext<T>(action.phase, action.ReadValue<T>());
        }

        public void RegistrationReadValue<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            var inputAction = GetAction(actionMap.ToString(), actionName.ToString());

            if (inputAction == null)
                return;

            if (isRegister)
            {
                if (_registeredReadActions.ContainsKey(action))
                    return;

                void UpdateAction()
                {
                    T value = inputAction.ReadValue<T>();

                    bool current = !EqualityComparer<T>.Default.Equals(value, default);
                    bool prev = _previousInputStates.GetValueOrDefault(inputAction);

                    InputActionPhase phase =
                        (!prev && current) ? InputActionPhase.Started :
                        (prev && current) ? InputActionPhase.Performed :
                        (prev && !current) ? InputActionPhase.Canceled :
                        InputActionPhase.Waiting;

                    _previousInputStates[inputAction] = current;

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

        #region Logical Registration (public API)

        public void RegistrationStarted<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            RegistrationLogical(actionMap, actionName, action, isRegister, InputActionPhase.Started);
        }

        public void RegistrationCancelled<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            RegistrationLogical(actionMap, actionName, action, isRegister, InputActionPhase.Canceled);
        }

        public void RegistrationPerformed<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            RegistrationLogical(actionMap, actionName, action, isRegister, InputActionPhase.Performed);
        }

        public void RegistrationStartCancelled<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            RegistrationLogical(actionMap, actionName, action, isRegister,
                InputActionPhase.Started,
                InputActionPhase.Canceled);
        }

        public void RegistrationAll<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where T : unmanaged where TAction : Enum
        {
            RegistrationLogical(actionMap, actionName, action, isRegister,
                InputActionPhase.Started,
                InputActionPhase.Performed,
                InputActionPhase.Canceled);
        }

        #endregion

        #region ExternalInput (NOT in interface but kept)

        public void RegisterExternalInput<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            IInputSource<T> inputSource)
            where T : unmanaged where TAction : Enum
        {
            var key = (object)(actionMap, actionName, inputSource);

            if (_externalInputCallbacks.ContainsKey(key))
                return;

            Action<InputContext<T>> callback = context =>
            {
                DispatchLogicalInput(
                    actionMap.ToString(),
                    actionName.ToString(),
                    context.Phase,
                    context.Value);
            };

            inputSource.RegisterAction(callback);
            _externalInputCallbacks.Add(key, callback);
        }

        public void UnregisterExternalInput<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            IInputSource<T> inputSource)
            where T : unmanaged where TAction : Enum
        {
            var key = (object)(actionMap, actionName, inputSource);

            if (!_externalInputCallbacks.TryGetValue(key, out var del))
                return;

            var callback = (Action<InputContext<T>>)del;

            inputSource.UnRegisterAction(callback);
            _externalInputCallbacks.Remove(key);
        }

        #endregion

        #region ActionMap (Interface public)

        public void SwitchActionMap(ActionMaps actionMap)
        {
            foreach (var map in _actionAsset.actionMaps)
                map.Disable();

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
            var list = new List<ActionMaps>();

            foreach (var map in _actionAsset.actionMaps)
            {
                if (!map.enabled) continue;

                if (Enum.TryParse(map.name, out ActionMaps parsed))
                    list.Add(parsed);
            }

            return list.ToArray();
        }

        public void EnableInput() => _actionAsset.Enable();
        public void DisableInput() => _actionAsset.Disable();

        #endregion

        #region Internal Core

        private void RegistrationLogical<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            Action<InputContext<T>> action,
            bool isRegister,
            params InputActionPhase[] phases)
            where T : unmanaged where TAction : Enum
        {
            foreach (var phase in phases)
            {
                var key = new InputEventKey(
                    actionMap.ToString(),
                    actionName.ToString(),
                    typeof(T),
                    phase);

                if (isRegister)
                {
                    if (_logicalInputEvents.TryGetValue(key, out var existing))
                        _logicalInputEvents[key] = Delegate.Combine(existing, action);
                    else
                        _logicalInputEvents.Add(key, action);
                }
                else
                {
                    if (!_logicalInputEvents.TryGetValue(key, out var existing))
                        continue;

                    var result = Delegate.Remove(existing, action);

                    if (result == null)
                        _logicalInputEvents.Remove(key);
                    else
                        _logicalInputEvents[key] = result;
                }
            }
        }

        private void DispatchLogicalInput<T>(
            string map,
            string name,
            InputActionPhase phase,
            T value)
            where T : unmanaged
        {
            var key = new InputEventKey(map, name, typeof(T), phase);

            if (!_logicalInputEvents.TryGetValue(key, out var callback))
                return;

            ((Action<InputContext<T>>)callback)
                ?.Invoke(new InputContext<T>(phase, value));
        }

        private InputAction GetAction(string map, string name)
            => _actionAsset.FindActionMap(map)?.FindAction(name);

        private InputActionMap FindMap(ActionMaps map)
            => _actionAsset.FindActionMap(map.ToString());

        #endregion

        #region Keys

        private readonly struct InputEventKey : IEquatable<InputEventKey>
        {
            private readonly string _map;
            private readonly string _name;
            private readonly Type _type;
            private readonly InputActionPhase _phase;

            public InputEventKey(string map, string name, Type type, InputActionPhase phase)
            {
                _map = map;
                _name = name;
                _type = type;
                _phase = phase;
            }

            public bool Equals(InputEventKey other)
                => _map == other._map
                && _name == other._name
                && _type == other._type
                && _phase == other._phase;

            public override bool Equals(object obj)
                => obj is InputEventKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(_map, _name, _type, _phase);
        }

        #endregion
    }
}