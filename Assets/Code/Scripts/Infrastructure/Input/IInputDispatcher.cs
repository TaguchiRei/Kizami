using System;
using UnityEngine.InputSystem;

public interface IInputDispatcher
{
    /// <summary>
    /// 値を直接読む
    /// </summary>
    /// <param name="actionMap"></param>
    /// <param name="actionName"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TAction"></typeparam>
    /// <returns></returns>
    InputContext<T> ReadValue<T, TAction>(ActionMaps actionMap, TAction actionName)
        where T : unmanaged
        where TAction : Enum;

    /// <summary>
    /// Actionの登録状態を変更する
    /// </summary>
    /// <param name="actionMap">ActionMapsを利用する</param>
    /// <param name="actionName">ActionMap名 + Actions のEnumを利用する</param>
    /// <param name="action">登録／解除するメソッド</param>
    /// <param name="isRegister">trueなら登録、falseなら解除</param>
    public void ChangeRegistrationStarted<TAction>(ActionMaps actionMap, TAction actionName,
        Action<InputAction.CallbackContext> action, bool isRegister) where TAction : Enum;

    /// <summary>
    /// Actionの登録状態を変更する
    /// </summary>
    /// <param name="actionMap">ActionMapsを利用する</param>
    /// <param name="actionName">ActionMap名 + Actions のEnumを利用する</param>
    /// <param name="action">登録／解除するメソッド</param>
    /// <param name="isRegister">trueなら登録、falseなら解除</param>
    public void ChangeRegistrationCancelled<TAction>(ActionMaps actionMap, TAction actionName,
        Action<InputAction.CallbackContext> action, bool isRegister) where TAction : Enum;

    /// <summary>
    /// Actionの登録状態を変更する
    /// </summary>
    /// <param name="actionMap">ActionMapsを利用する</param>
    /// <param name="actionName">ActionMap名 + Actions のEnumを利用する</param>
    /// <param name="action">登録／解除するメソッド</param>
    /// <param name="isRegister">trueなら登録、falseなら解除</param>
    public void ChangeRegistrationStartCancelled<TAction>(ActionMaps actionMap, TAction actionName,
        Action<InputAction.CallbackContext> action, bool isRegister) where TAction : Enum;

    /// <summary>
    /// 他のActionMapをすべて無効化し、一つのActionMapのみ有効化する
    /// </summary>
    /// <param name="actionMap">ActionMapsを利用する</param>
    public void SwitchActionMap(ActionMaps actionMap);

    /// <summary>
    /// ActionMapを追加で有効化する
    /// </summary>
    /// <param name="actionMap"></param>
    public void EnableActionMap(ActionMaps actionMap);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="actionMap"></param>
    public void DisableActionMap(ActionMaps actionMap);

    /// <summary>
    /// 現在有効なActionMapを取得する
    /// </summary>
    /// <returns></returns>
    public ActionMaps[] GetActiveActionMap();

    /// <summary>
    /// 入力を有効化する
    /// </summary>
    public void EnableInput();

    /// <summary>
    /// 入力を無効化する
    /// </summary>
    public void DisableInput();
}

public enum Registration
{
    Register,
    UnRegister
}