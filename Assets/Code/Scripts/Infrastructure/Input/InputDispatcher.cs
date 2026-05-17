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

        private readonly Dictionary<Delegate, Action<InputAction.CallbackContext>>
            _registeredInputActions = new();

        /// <summary>
        /// Polling入力用の前フレーム状態保持
        /// </summary>
        private readonly Dictionary<InputAction, bool> _previousInputStates = new();

        public override void Initialize()
        {
            base.Initialize();
            _actionAsset.Enable();
        }

        private void Update()
        {
            foreach (var updateAction in _registeredReadActions.Values)
                updateAction();
        }

        private void OnDestroy()
        {
            _registeredReadActions.Clear();

            foreach (var pair in _registeredInputActions)
            {
                foreach (var map in _actionAsset.actionMaps)
                {
                    foreach (var action in map.actions)
                    {
                        action.started -= pair.Value;
                        action.performed -= pair.Value;
                        action.canceled -= pair.Value;
                    }
                }
            }

            _registeredInputActions.Clear();
            _previousInputStates.Clear();

            _actionAsset.Disable();
        }

        public InputContext<T> ReadValue<T, TAction>(
            ActionMaps actionMap,
            TAction actionName)
            where T : unmanaged
            where TAction : Enum
        {
            var action = GetAction(actionMap.ToString(), actionName.ToString());

            if (action == null)
            {
                Debug.LogWarning(
                    $"[InputDispatcher] {actionMap}.{actionName} は見つかりませんでした。");

                return new InputContext<T>(
                    InputActionPhase.Disabled,
                    default);
            }

            return new InputContext<T>(
                action.phase,
                action.ReadValue<T>());
        }

        public void RegistrationReadValue<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            Action<InputContext<T>> action,
            bool isRegister)
            where T : unmanaged
            where TAction : Enum
        {
            var inputAction = GetAction(
                actionMap.ToString(),
                actionName.ToString());

            if (inputAction == null)
            {
                Debug.LogWarning(
                    $"[InputDispatcher] {actionMap}.{actionName} は見つかりませんでした。");

                return;
            }

            if (isRegister)
            {
                if (_registeredReadActions.ContainsKey(action))
                    return;

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

                    action?.Invoke(
                        new InputContext<T>(
                            phase,
                            value));
                }

                _registeredReadActions.Add(action, UpdateAction);
            }
            else
            {
                _registeredReadActions.Remove(action);
            }
        }

        public void RegistrationStarted<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            Action<InputContext<T>> action,
            bool isRegister)
            where T : unmanaged
            where TAction : Enum
        {
            RegistrationPhase(
                actionMap,
                actionName,
                action,
                isRegister,
                InputPhaseType.Started);
        }

        public void RegistrationCancelled<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            Action<InputContext<T>> action,
            bool isRegister)
            where T : unmanaged
            where TAction : Enum
        {
            RegistrationPhase(
                actionMap,
                actionName,
                action,
                isRegister,
                InputPhaseType.Canceled);
        }

        public void RegistrationPerformed<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            Action<InputContext<T>> action,
            bool isRegister)
            where T : unmanaged
            where TAction : Enum
        {
            RegistrationPhase(
                actionMap,
                actionName,
                action,
                isRegister,
                InputPhaseType.Performed);
        }

        public void RegistrationStartCancelled<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            Action<InputContext<T>> action,
            bool isRegister)
            where T : unmanaged
            where TAction : Enum
        {
            RegistrationPhase(
                actionMap,
                actionName,
                action,
                isRegister,
                InputPhaseType.Started,
                InputPhaseType.Canceled);
        }

        public void RegistrationAll<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            Action<InputContext<T>> action,
            bool isRegister)
            where T : unmanaged
            where TAction : Enum
        {
            RegistrationPhase(
                actionMap,
                actionName,
                action,
                isRegister,
                InputPhaseType.Started,
                InputPhaseType.Performed,
                InputPhaseType.Canceled);
        }

        public void SwitchActionMap(ActionMaps actionMap)
        {
            foreach (var map in _actionAsset.actionMaps)
                map.Disable();

            FindMap(actionMap)?.Enable();
        }

        public void EnableActionMap(ActionMaps actionMap)
            => FindMap(actionMap)?.Enable();

        public void DisableActionMap(ActionMaps actionMap)
            => FindMap(actionMap)?.Disable();

        public ActionMaps[] GetActiveActionMap()
        {
            var activeMaps = new List<ActionMaps>();

            foreach (var map in _actionAsset.actionMaps)
            {
                if (!map.enabled)
                    continue;

                if (Enum.TryParse(map.name, out ActionMaps parsed))
                {
                    activeMaps.Add(parsed);
                }
                else
                {
                    Debug.LogWarning(
                        $"[InputDispatcher] ActionMap {map.name} は Enum に存在しません。");
                }
            }

            return activeMaps.ToArray();
        }

        public void EnableInput() => _actionAsset.Enable();

        public void DisableInput() => _actionAsset.Disable();

        private void RegistrationPhase<T, TAction>(
            ActionMaps actionMap,
            TAction actionName,
            Action<InputContext<T>> action,
            bool isRegister,
            params InputPhaseType[] phaseTypes)
            where T : unmanaged
            where TAction : Enum
        {
            var inputAction = GetAction(
                actionMap.ToString(),
                actionName.ToString());

            if (inputAction == null)
            {
                Debug.LogWarning(
                    $"[InputDispatcher] {actionMap}.{actionName} は見つかりませんでした。");

                return;
            }

            if (isRegister)
            {
                if (_registeredInputActions.ContainsKey(action))
                    return;

                void Callback(InputAction.CallbackContext context)
                {
                    action?.Invoke(
                        new InputContext<T>(
                            context.phase,
                            context.ReadValue<T>()));
                }

                _registeredInputActions.Add(action, Callback);

                foreach (var phase in phaseTypes)
                {
                    switch (phase)
                    {
                        case InputPhaseType.Started:
                            inputAction.started += Callback;
                            break;

                        case InputPhaseType.Performed:
                            inputAction.performed += Callback;
                            break;

                        case InputPhaseType.Canceled:
                            inputAction.canceled += Callback;
                            break;
                    }
                }
            }
            else
            {
                if (!_registeredInputActions.TryGetValue(
                        action,
                        out var callback))
                    return;

                foreach (var phase in phaseTypes)
                {
                    switch (phase)
                    {
                        case InputPhaseType.Started:
                            inputAction.started -= callback;
                            break;

                        case InputPhaseType.Performed:
                            inputAction.performed -= callback;
                            break;

                        case InputPhaseType.Canceled:
                            inputAction.canceled -= callback;
                            break;
                    }
                }

                _registeredInputActions.Remove(action);
            }
        }

        private InputActionMap FindMap(ActionMaps actionMap)
        {
            var map = _actionAsset.FindActionMap(actionMap.ToString());

            if (map == null)
            {
                Debug.LogWarning(
                    $"[InputDispatcher] ActionMap {actionMap} は見つかりませんでした。");
            }

            return map;
        }

        private InputAction GetAction(
            string actionMap,
            string actionName)
        {
            return _actionAsset
                .FindActionMap(actionMap)
                ?.FindAction(actionName);
        }

        private enum InputPhaseType
        {
            Started,
            Performed,
            Canceled
        }
    }
}