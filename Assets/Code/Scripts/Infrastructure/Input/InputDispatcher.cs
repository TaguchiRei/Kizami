using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputDispatcher : InitializableMonoBehaviour, IInputDispatcher
{
    [SerializeField] private InputActionAsset _actionAsset;

    public override void Initialize()
    {
        base.Initialize();
        _actionAsset.Enable();
    }

    private void OnDestroy()
    {
        _actionAsset.Disable();
    }

    public InputContext<T> ReadValue<T, TAction>(ActionMaps actionMap, TAction actionName)
        where T : unmanaged
        where TAction : Enum
    {
        var action = GetAction(actionMap.ToString(), actionName.ToString());
        if (action == null)
        {
            Debug.LogWarning($"[InputDispatcher] {actionMap}.{actionName} は見つかりませんでした。");
            return new InputContext<T>(InputActionPhase.Disabled, default);
        }

        return new InputContext<T>(action.phase, action.ReadValue<T>());
    }

    public void ChangeRegistrationStarted<TAction>(ActionMaps actionMap, TAction actionName,
        Action<InputAction.CallbackContext> action, bool isRegister) where TAction : Enum
    {
        var inputAction = GetAction(actionMap.ToString(), actionName.ToString());
        if (inputAction == null)
        {
            Debug.LogWarning($"[InputDispatcher] {actionMap}.{actionName} は見つかりませんでした。");
            return;
        }

        if (isRegister) inputAction.started += action;
        else inputAction.started -= action;
    }

    public void ChangeRegistrationCancelled<TAction>(ActionMaps actionMap, TAction actionName,
        Action<InputAction.CallbackContext> action, bool isRegister) where TAction : Enum
    {
        var inputAction = GetAction(actionMap.ToString(), actionName.ToString());
        if (inputAction == null)
        {
            Debug.LogWarning($"[InputDispatcher] {actionMap}.{actionName} は見つかりませんでした。");
            return;
        }

        if (isRegister) inputAction.canceled += action;
        else inputAction.canceled -= action;
    }

    public void ChangeRegistrationStartCancelled<TAction>(ActionMaps actionMap, TAction actionName,
        Action<InputAction.CallbackContext> action, bool isRegister) where TAction : Enum
    {
        var inputAction = GetAction(actionMap.ToString(), actionName.ToString());
        if (inputAction == null)
        {
            Debug.LogWarning($"[InputDispatcher] {actionMap}.{actionName} は見つかりませんでした。");
            return;
        }

        if (isRegister)
        {
            inputAction.started += action;
            inputAction.canceled += action;
        }
        else
        {
            inputAction.started -= action;
            inputAction.canceled -= action;
        }
    }

    public void SwitchActionMap(ActionMaps actionMap)
    {
        foreach (var map in _actionAsset.actionMaps) map.Disable();
        FindMap(actionMap)?.Enable();
    }

    public void EnableActionMap(ActionMaps actionMap) => FindMap(actionMap)?.Enable();
    public void DisableActionMap(ActionMaps actionMap) => FindMap(actionMap)?.Disable();

    public ActionMaps[] GetActiveActionMap()
    {
        var activeMaps = new System.Collections.Generic.List<ActionMaps>();
        foreach (var map in _actionAsset.actionMaps)
        {
            if (!map.enabled) continue;
            if (Enum.TryParse(map.name, out ActionMaps parsed)) activeMaps.Add(parsed);
            else Debug.LogWarning($"[InputDispatcher] ActionMap {map.name} は Enum に存在しません。");
        }

        return activeMaps.ToArray();
    }

    public void EnableInput() => _actionAsset.Enable();
    public void DisableInput() => _actionAsset.Disable();

    private InputActionMap FindMap(ActionMaps actionMap)
    {
        var map = _actionAsset.FindActionMap(actionMap.ToString());
        if (map == null) Debug.LogWarning($"[InputDispatcher] ActionMap {actionMap} は見つかりませんでした。");
        return map;
    }

    private InputAction GetAction(string actionMap, string actionName)
    {
        return _actionAsset.FindActionMap(actionMap)?.FindAction(actionName);
    }
}