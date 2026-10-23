using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using UnityEngine.InputSystem;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// Player の ActionMap のキーボード・マウスの割り当てを、実行中に割り当て直す Adapter。
    /// 割り当て直しは InputActionAsset の上書きとして持ち、変更のたびに差分を IOperationSettingController へ保存する。
    /// Dispatcher と同じ InputActionAsset を渡すことで、変更がインゲームの操作へそのまま効く。
    /// 動きの入力（Look・CutRotate）と、キーボード・マウス以外の割り当ては対象にしない。
    /// </summary>
    public sealed class InputRebindAdapter
    {
        /// <summary>
        /// 割り当て直せる割り当て 1 つ分。
        /// </summary>
        public readonly struct Entry
        {
            /// <summary> 画面に出す名前 </summary>
            public readonly string Label;

            /// <summary> 割り当て先のアクション </summary>
            public readonly InputAction Action;

            /// <summary> アクションの中での割り当ての位置 </summary>
            public readonly int BindingIndex;

            public Entry(string label, InputAction action, int bindingIndex)
            {
                Label = label;
                Action = action;
                BindingIndex = bindingIndex;
            }
        }

        private const string ACTION_MAP_NAME = "Player";
        private const string BINDING_GROUP = "Keyboard&Mouse";
        private const string CANCEL_PATH = "<Keyboard>/escape";

        private static readonly string[] _rebindableActionNames =
        {
            "Move", "Attack", "Interact", "Jump", "Sprint", "Warp", "SlowMode", "Throw", "Skill1", "Skill2", "Skill3",
        };

        private readonly InputActionAsset _asset;
        private readonly IOperationSettingController _controller;
        private readonly List<Entry> _entries = new();
        private InputActionRebindingExtensions.RebindingOperation _operation;

        /// <summary> 割り当て直せる割り当て </summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary> 割り当て直しの入力待ちか </summary>
        public bool IsRebinding => _operation != null;

        /// <param name="asset">割り当てを変える InputActionAsset。Dispatcher が使うものと同じ実体</param>
        /// <param name="controller">差分の保存先と、保存済みの差分の取得元</param>
        public InputRebindAdapter(InputActionAsset asset, IOperationSettingController controller)
        {
            _asset = asset;
            _controller = controller;
            BuildEntries();
        }

        /// <summary>
        /// 保存済みの差分を InputActionAsset へ当てる。
        /// </summary>
        public void ApplySaved()
        {
            if (_asset == null || string.IsNullOrEmpty(_controller.BindingOverridesJson)) return;

            _asset.LoadBindingOverridesFromJson(_controller.BindingOverridesJson);
        }

        /// <summary>
        /// 割り当ての今のキーの表示名を返す。
        /// </summary>
        public string GetDisplayName(Entry entry)
        {
            return entry.Action.GetBindingDisplayString(entry.BindingIndex);
        }

        /// <summary>
        /// 次に押したキーを割り当てる入力待ちを始める。Escape で取り消す。
        /// 割り当てたキーがほかの割り当てと重なったときは、ほかの割り当てに元のキーを譲って入れ替える。
        /// </summary>
        /// <param name="entry">割り当て直す割り当て</param>
        /// <param name="onFinished">割り当て直した、または取り消したときに呼ぶ処理</param>
        public void StartRebind(Entry entry, Action onFinished)
        {
            if (IsRebinding) return;

            var action = entry.Action;
            var wasEnabled = action.enabled;
            if (wasEnabled) action.Disable();
            var oldPath = action.bindings[entry.BindingIndex].effectivePath;

            void Finish(bool isChanged)
            {
                _operation?.Dispose();
                _operation = null;
                if (wasEnabled) action.Enable();
                if (isChanged) SwapDuplicate(entry, oldPath);
                onFinished?.Invoke();
            }

            _operation = action.PerformInteractiveRebinding(entry.BindingIndex)
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough(CANCEL_PATH)
                .OnComplete(_ => Finish(true))
                .OnCancel(_ => Finish(false))
                .Start();
        }

        /// <summary>
        /// 割り当てをすべて初期値に戻して保存する。
        /// </summary>
        public void ResetAll()
        {
            if (_asset == null) return;

            _asset.RemoveAllBindingOverrides();
            _controller.SetBindingOverridesJson(string.Empty);
        }

        private void BuildEntries()
        {
            var map = _asset != null ? _asset.FindActionMap(ACTION_MAP_NAME) : null;
            if (map == null) return;

            var labelCounts = new Dictionary<string, int>();
            foreach (var actionName in _rebindableActionNames)
            {
                var action = map.FindAction(actionName);
                if (action == null) continue;

                labelCounts.Clear();
                for (var i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    if (binding.isComposite || !IsKeyboardMouse(binding)) continue;

                    var label = binding.isPartOfComposite ? $"{action.name} {binding.name}" : action.name;
                    labelCounts[label] = labelCounts.GetValueOrDefault(label) + 1;
                    _entries.Add(new Entry(labelCounts[label] > 1 ? $"{label} {labelCounts[label]}" : label, action, i));
                }
            }
        }

        private static bool IsKeyboardMouse(InputBinding binding)
        {
            return !string.IsNullOrEmpty(binding.groups) && binding.groups.Contains(BINDING_GROUP);
        }

        /// <summary>
        /// 割り当て直した結果、別の割り当てと同じキーになったときは、別の割り当てへ元のキーを譲る。そのうえで差分を保存する。
        /// </summary>
        private void SwapDuplicate(Entry changed, string oldPath)
        {
            var newPath = changed.Action.bindings[changed.BindingIndex].effectivePath;
            foreach (var other in _entries)
            {
                if (other.Action == changed.Action && other.BindingIndex == changed.BindingIndex) continue;

                if (other.Action.bindings[other.BindingIndex].effectivePath == newPath)
                {
                    other.Action.ApplyBindingOverride(other.BindingIndex, oldPath);
                }
            }

            _controller.SetBindingOverridesJson(_asset.SaveBindingOverridesAsJson());
        }
    }
}
