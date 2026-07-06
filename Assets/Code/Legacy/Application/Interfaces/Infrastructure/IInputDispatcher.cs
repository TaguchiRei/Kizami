using System;
using UsefulTools.AutoGenerate;

namespace UsefulTools.Infrastructure.Runtime.Input
{
    public interface IInputDispatcher
    {
        /// <summary>
        /// 値を直接読む
        /// </summary>
        /// <param name="actionMap">ActionMapsを利用する</param>
        /// <param name="actionName">ActionMap名 + Actions のEnumを利用する</param>
        /// <returns></returns>
        InputContext<T> ReadValue<T, TAction>(ActionMaps actionMap, TAction actionName)
            where T : unmanaged
            where TAction : Enum;

        /// <summary>
        /// 値を直接読む方式の入力に登録する。
        /// 一部の入力に対するバグの対策として利用する。
        /// </summary>
        /// <param name="actionMap">ActionMapsを利用する</param>
        /// <param name="actionName">ActionMap名 + Actions のEnumを利用する</param>
        /// <param name="action">登録／解除するメソッド</param>
        /// <param name="isRegister">trueなら登録、falseなら解除</param>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TAction">ActionMapと対応するActionの名前を指定</typeparam>
        public void RegistrationReadValue<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister) where TAction : Enum where T : unmanaged;

        /// <summary>
        /// Startedフェーズに対するActionの登録状態を変更する
        /// </summary>
        /// <param name="actionMap">ActionMapsを利用する</param>
        /// <param name="actionName">ActionMap名 + Actions のEnumを利用する</param>
        /// <param name="action">登録／解除するメソッド</param>
        /// <param name="isRegister">trueなら登録、falseなら解除</param>
        public void RegistrationStarted<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister) where TAction : Enum where T : unmanaged;

        /// <summary>
        /// Cancelledフェーズに対するActionの登録状態を変更する
        /// </summary>
        /// <param name="actionMap">ActionMapsを利用する</param>
        /// <param name="actionName">ActionMap名 + Actions のEnumを利用する</param>
        /// <param name="action">登録／解除するメソッド</param>
        /// <param name="isRegister">trueなら登録、falseなら解除</param>
        public void RegistrationCancelled<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister) where TAction : Enum where T : unmanaged;

        /// <summary>
        /// StartedおよびCancelledフェーズに対するActionの登録状態を変更する
        /// </summary>
        /// <param name="actionMap">ActionMapsを利用する</param>
        /// <param name="actionName">ActionMap名 + Actions のEnumを利用する</param>
        /// <param name="action">登録／解除するメソッド</param>
        /// <param name="isRegister">trueなら登録、falseなら解除</param>
        public void RegistrationStartCancelled<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister) where TAction : Enum where T : unmanaged;

        /// <summary>
        /// Performedフェーズに対するActionの登録状態を変更する
        /// </summary>
        /// <param name="actionMap">ActionMapsを利用する</param>
        /// <param name="actionName">ActionMap名 + Actions のEnumを利用する</param>
        /// <param name="action">登録／解除するメソッド</param>
        /// <param name="isRegister">trueなら登録、falseなら解除</param>
        public void RegistrationPerformed<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister) where TAction : Enum where T : unmanaged;

        /// <summary>
        /// すべてのフェーズに対するActionの登録状態を変更する
        /// </summary>
        /// <param name="actionMap">ActionMapsを利用する</param>
        /// <param name="actionName">ActionMap名 + Actions のEnumを利用する</param>
        /// <param name="action">登録／解除するメソッド</param>
        /// <param name="isRegister">trueなら登録、falseなら解除</param>
        public void RegistrationAll<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister) where TAction : Enum where T : unmanaged;

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
        /// 指定したアクションマップを無効化する
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
}