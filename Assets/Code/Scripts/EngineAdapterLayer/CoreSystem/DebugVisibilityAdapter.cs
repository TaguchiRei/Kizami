using UnityEngine;
using UnityEngine.InputSystem;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// デバッグ用の表示と操作を、F3 で出し入れする Adapter。常駐シーンの DebugGUI と同じ GameObject へ置く。
    /// 既定は隠す。隠している間は DebugGUI を無効にして描画を止め、デバッグ用の Initializer は IsVisible を見て自前の表示と操作を止める。
    /// エディタと Development Build 以外では、キーを読まず常に隠す。
    /// </summary>
    public sealed class DebugVisibilityAdapter : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("出し入れする DebugGUI。隠している間は無効にする")]
        private Behaviour _debugGui;

        /// <summary> デバッグ用の表示と操作を出している間か。デバッグ専用の為、State にはしない </summary>
        public static bool IsVisible { get; private set; }

        private void Awake()
        {
            IsVisible = false;
            Apply();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f3Key.wasPressedThisFrame) return;

            IsVisible = !IsVisible;
            Apply();
        }
#endif

        private void OnDestroy()
        {
            IsVisible = false;
        }

        private void Apply()
        {
            if (_debugGui != null) _debugGui.enabled = IsVisible;
        }
    }
}
