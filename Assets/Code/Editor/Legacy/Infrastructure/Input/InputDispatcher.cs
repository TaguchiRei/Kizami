// [Legacy] 作り直しに伴い全体を無効化
#if false
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

        // InputSystem用のキャッシュ
        private readonly Dictionary<object, ActionData> _callbackCache = new();

        #region 外部入力（ExternalInput）の管理構造

        // キーはExternalActions、値はインターフェースの実体
        private readonly Dictionary<ExternalActions, object> _externalSources = new();

        #endregion

        public override void Initialize()
        {
            base.Initialize();
            _actionAsset.Enable();
        }

        private void OnDestroy()
        {
            _actionAsset.Disable();
            _callbackCache.Clear();
            _externalSources.Clear();
        }

        public InputContext<T> ReadValue<T, TAction>(ActionMaps actionMap, TAction actionName)
            where T : unmanaged where TAction : Enum
        {
            var action = GetAction(actionMap.ToString(), actionName.ToString());
            if (action == null) return new InputContext<T>(InputActionPhase.Disabled, default);
            return new InputContext<T>(action.phase, action.ReadValue<T>());
        }

        #region インフラレイヤー向け：外部入力ソース（IInputSource）の着脱 API

        /// <summary>
        /// MobileInput などの入力経路自体を登録する
        /// </summary>
        public void RegisterExternalInput<T>(ActionMaps actionMap, ExternalActions actionName,
            IInputSource<T> inputSource)
            where T : unmanaged
        {
            _externalSources[actionName] = inputSource;
            Debug.Log($"[InputDispatcher] ExternalInputSource 登録: {actionName}");
        }

        /// <summary>
        /// 外部入力経路を解除する
        /// </summary>
        public void UnregisterExternalInput<T>(ActionMaps actionMap, ExternalActions actionName,
            IInputSource<T> inputSource)
            where T : unmanaged
        {
            if (_externalSources.TryGetValue(actionName, out var source) && source == inputSource)
            {
                _externalSources.Remove(actionName);
                Debug.Log($"[InputDispatcher] ExternalInputSource 解除: {actionName}");
            }
        }

        #endregion

        #region 各フェーズへの登録・解除の制御 (ExternalInput その場登録対応)

        public void RegistrationReadValue<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where TAction : Enum where T : unmanaged
        {
            RegistrationPerformed(actionMap, actionName, action, isRegister);
        }

        public void RegistrationStarted<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where TAction : Enum where T : unmanaged
        {
            // 引数の Enum が ExternalActions だった場合は、その場でソースに直接登録・解除する
            if (actionName is ExternalActions extAction)
            {
                InvokeExternalRegistration(extAction, action, isRegister);
                return;
            }

            // 以下、既存のInputSystem用の処理
            var inputAction = GetRequiredAction(actionMap, actionName);
            if (inputAction == null) return;

            if (isRegister)
            {
                var unityAction = GetOrCreateCache(action);
                inputAction.started += unityAction;
            }
            else
            {
                if (_callbackCache.TryGetValue(action, out var unityAction))
                {
                    inputAction.started -= unityAction.Callback;
                    TryRemoveCache(action);
                }
            }
        }

        public void RegistrationCancelled<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where TAction : Enum where T : unmanaged
        {
            if (actionName is ExternalActions extAction)
            {
                InvokeExternalRegistration(extAction, action, isRegister);
                return;
            }

            var inputAction = GetRequiredAction(actionMap, actionName);
            if (inputAction == null) return;

            if (isRegister)
            {
                var unityAction = GetOrCreateCache(action);
                inputAction.canceled += unityAction;
            }
            else
            {
                if (_callbackCache.TryGetValue(action, out var unityAction))
                {
                    inputAction.canceled -= unityAction.Callback;
                    TryRemoveCache(action);
                }
            }
        }

        public void RegistrationPerformed<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where TAction : Enum where T : unmanaged
        {
            if (actionName is ExternalActions extAction)
            {
                InvokeExternalRegistration(extAction, action, isRegister);
                return;
            }

            var inputAction = GetRequiredAction(actionMap, actionName);
            if (inputAction == null) return;

            if (isRegister)
            {
                var unityAction = GetOrCreateCache(action);
                inputAction.performed += unityAction;
            }
            else
            {
                if (_callbackCache.TryGetValue(action, out var unityAction))
                {
                    inputAction.performed -= unityAction.Callback;
                    TryRemoveCache(action);
                }
            }
        }

        public void RegistrationStartCancelled<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where TAction : Enum where T : unmanaged
        {
            if (actionName is ExternalActions extAction)
            {
                InvokeExternalRegistration(extAction, action, isRegister);
                return;
            }

            var inputAction = GetRequiredAction(actionMap, actionName);
            if (inputAction == null) return;

            if (isRegister)
            {
                var unityAction = GetOrCreateCache(action);
                inputAction.started += unityAction;
                inputAction.canceled += unityAction;
            }
            else
            {
                if (_callbackCache.TryGetValue(action, out var unityAction))
                {
                    inputAction.started -= unityAction.Callback;
                    inputAction.canceled -= unityAction.Callback;
                    TryRemoveCache(action);
                }
            }
        }

        public void RegistrationAll<T, TAction>(ActionMaps actionMap, TAction actionName,
            Action<InputContext<T>> action, bool isRegister)
            where TAction : Enum where T : unmanaged
        {
            if (actionName is ExternalActions extAction)
            {
                InvokeExternalRegistration(extAction, action, isRegister);
                return;
            }

            var inputAction = GetRequiredAction(actionMap, actionName);
            if (inputAction == null) return;

            if (isRegister)
            {
                var unityAction = GetOrCreateCache(action);
                inputAction.started += unityAction;
                inputAction.performed += unityAction;
                inputAction.canceled += unityAction;
            }
            else
            {
                if (_callbackCache.TryGetValue(action, out var unityAction))
                {
                    inputAction.started -= unityAction.Callback;
                    inputAction.performed -= unityAction.Callback;
                    inputAction.canceled -= unityAction.Callback;
                    TryRemoveCache(action);
                }
            }
        }

        #endregion

        #region 外部入力ソースへのダイレクト登録ロジック

        private void InvokeExternalRegistration<T>(ExternalActions extAction, Action<InputContext<T>> action,
            bool isRegister) where T : unmanaged
        {
            if (!_externalSources.TryGetValue(extAction, out var sourceObj))
            {
                Debug.LogWarning($"[InputDispatcher] ExternalInputSource {extAction} はまだ登録されていません。");
                return;
            }

            if (sourceObj is IInputSource<T> inputSource)
            {
                if (isRegister)
                {
                    inputSource.RegisterAction(action);
                }
                else
                {
                    inputSource.UnRegisterAction(action);
                }
            }
            else
            {
                Debug.LogError(
                    $"[InputDispatcher] {extAction} のソースの型 ({sourceObj.GetType().Name}) が、要求された型 ({typeof(IInputSource<T>).Name}) と一致しません。");
            }
        }

        #endregion

        #region インフラ共通ヘルパー・キャッシュ管理

        private Action<InputAction.CallbackContext> GetOrCreateCache<T>(Action<InputContext<T>> appAction)
            where T : unmanaged
        {
            if (!_callbackCache.TryGetValue(appAction, out var data))
            {
                Action<InputAction.CallbackContext> unityAction = ctx =>
                    appAction(new InputContext<T>(ctx.phase, ctx.ReadValue<T>()));

                data = new ActionData(unityAction, 0);
                _callbackCache[appAction] = data;
            }

            data.RegisterCount++;
            return data.Callback;
        }

        private void TryRemoveCache(object appAction)
        {
            if (!_callbackCache.TryGetValue(appAction, out var data))
                return;

            data.RegisterCount--;

            if (data.RegisterCount <= 0)
            {
                _callbackCache.Remove(appAction);
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
            var activeMaps = new List<ActionMaps>();
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

        private InputAction GetRequiredAction<TAction>(ActionMaps actionMap, TAction actionName) where TAction : Enum
        {
            var action = GetAction(actionMap.ToString(), actionName.ToString());
            if (action == null) Debug.LogWarning($"[InputDispatcher] {actionMap}.{actionName} は見つかりませんでした。");
            return action;
        }

        #endregion

        private class ActionData
        {
            public int RegisterCount;
            public readonly Action<InputAction.CallbackContext> Callback;

            public ActionData(Action<InputAction.CallbackContext> context, int count)
            {
                RegisterCount = count;
                Callback = context;
            }
        }
    }
}
#endif
