using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.UI;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// コンフィグの画面。左に操作の設定（ダッシュ・つかむの受け付け方、切断面の回転角度、視点感度）と音量、右にキーの割り当てを並べる。
    /// OutGameMenuAdapter が用意した 1 つのパネルへ組み立て、値の変更は IOperationSettingController へ渡す。
    /// キーの割り当ては InputRebindAdapter が渡されたときだけ出す。
    /// </summary>
    public sealed class ConfigScreen
    {
        private const int LABEL_FONT_SIZE = 28;
        private const int BUTTON_FONT_SIZE = 30;
        private const float LEFT_X = 100f;
        private const float LEFT_WIDTH = 800f;
        private const float LABEL_WIDTH = 340f;
        private const float ROW_HEIGHT = 70f;
        private const float ROW_GAP = 10f;
        private const float KEY_X = 1000f;
        private const float KEY_WIDTH = 820f;
        private const float KEY_HEIGHT = 44f;
        private const float KEY_GAP = 4f;
        private const float TOP_Y = 60f;
        private const float BOTTOM_Y = 820f;
        private const float BOTTOM_BUTTON_WIDTH = 380f;
        private const float MIN_SENSITIVITY = 0.1f;
        private const float MAX_SENSITIVITY = 5f;
        private const float MIN_ANGLE = 1f;
        private const float MAX_ANGLE = 90f;

        private readonly Transform _panel;
        private readonly IOperationSettingState _state;
        private readonly IOperationSettingController _controller;
        private readonly InputRebindAdapter _rebind;
        private readonly Color _buttonColor;
        private readonly Color _accentColor;
        private readonly List<Text> _keyLabels = new();

        /// <param name="panel">組み立て先のパネル。基準の解像度いっぱいに広がっていること</param>
        /// <param name="state">表示する設定の値の取得元</param>
        /// <param name="controller">設定を変える窓口</param>
        /// <param name="rebind">キーの割り当てを変える Adapter。なければキーの割り当ては出さない</param>
        /// <param name="buttonColor">ボタンの色</param>
        /// <param name="accentColor">スライダーの塗りの色</param>
        /// <param name="back">戻るボタンを押したときに呼ぶ処理</param>
        public ConfigScreen(Transform panel, IOperationSettingState state, IOperationSettingController controller,
            InputRebindAdapter rebind, Color buttonColor, Color accentColor, Action back)
        {
            _panel = panel;
            _state = state;
            _controller = controller;
            _rebind = rebind;
            _buttonColor = buttonColor;
            _accentColor = accentColor;

            BuildSettings();
            BuildKeys();
            BuildBottom(back);
        }

        private static void PlaceAtTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private static float GetRowY(int row)
        {
            return TOP_Y + row * (ROW_HEIGHT + ROW_GAP);
        }

        private static string GetSprintText(SprintInputMode mode)
        {
            return $"Dash Input: {(mode == SprintInputMode.Hold ? "Hold" : "Toggle")}";
        }

        private static string GetGrabText(GrabInputMode mode)
        {
            return $"Grab Input: {(mode == GrabInputMode.Hold ? "Hold" : "Toggle")}";
        }

        private void BuildSettings()
        {
            var sprintButton = CreateButton("SprintButton", LEFT_X, GetRowY(0), LEFT_WIDTH, ROW_HEIGHT, string.Empty);
            var sprintLabel = sprintButton.GetComponentInChildren<Text>();
            sprintLabel.text = GetSprintText(_state.SprintInputMode);
            sprintButton.onClick.AddListener(() =>
            {
                var next = _state.SprintInputMode == SprintInputMode.Hold ? SprintInputMode.Toggle : SprintInputMode.Hold;
                _controller.SetSprintInputMode(next);
                sprintLabel.text = GetSprintText(next);
            });

            var grabButton = CreateButton("GrabButton", LEFT_X, GetRowY(1), LEFT_WIDTH, ROW_HEIGHT, string.Empty);
            var grabLabel = grabButton.GetComponentInChildren<Text>();
            grabLabel.text = GetGrabText(_state.GrabInputMode);
            grabButton.onClick.AddListener(() =>
            {
                var next = _state.GrabInputMode == GrabInputMode.Hold ? GrabInputMode.Toggle : GrabInputMode.Hold;
                _controller.SetGrabInputMode(next);
                grabLabel.text = GetGrabText(next);
            });

            CreateSliderRow(2, "Cut Rotate Angle", MIN_ANGLE, MAX_ANGLE, true, _state.CutRotateStepAngle, "0",
                _controller.SetCutRotateStepAngle);
            CreateSliderRow(3, "Horizontal Sensitivity", MIN_SENSITIVITY, MAX_SENSITIVITY, false,
                _state.HorizontalSensitivity, "0.0", _controller.SetHorizontalSensitivity);
            CreateSliderRow(4, "Vertical Sensitivity", MIN_SENSITIVITY, MAX_SENSITIVITY, false,
                _state.VerticalSensitivity, "0.0", _controller.SetVerticalSensitivity);
            CreateSliderRow(5, "Master Volume", 0f, OperationSettingState.MAX_VOLUME, true, _state.MasterVolume, "0",
                value => _controller.SetMasterVolume(Mathf.RoundToInt(value)));
            CreateSliderRow(6, "BGM Volume", 0f, OperationSettingState.MAX_VOLUME, true, _state.BgmVolume, "0",
                value => _controller.SetBgmVolume(Mathf.RoundToInt(value)));
            CreateSliderRow(7, "SE Volume", 0f, OperationSettingState.MAX_VOLUME, true, _state.SeVolume, "0",
                value => _controller.SetSeVolume(Mathf.RoundToInt(value)));
        }

        private void BuildKeys()
        {
            var heading = UiBuilder.CreateText(_panel, "KeyHeading", LABEL_FONT_SIZE, TextAnchor.MiddleLeft);
            heading.text = _rebind != null ? "Key Config (Esc: cancel)" : "Key Config is unavailable";
            PlaceAtTopLeft(heading.rectTransform, KEY_X, 10f, KEY_WIDTH, 40f);
            if (_rebind == null) return;

            for (var i = 0; i < _rebind.Entries.Count; i++)
            {
                var entry = _rebind.Entries[i];
                var button = CreateButton($"Key{i}Button", KEY_X, TOP_Y + i * (KEY_HEIGHT + KEY_GAP), KEY_WIDTH,
                    KEY_HEIGHT, string.Empty);
                var keyLabel = button.GetComponentInChildren<Text>();
                _keyLabels.Add(keyLabel);
                button.onClick.AddListener(() => StartRebind(entry, keyLabel));
            }

            RefreshKeys();
        }

        private void BuildBottom(Action back)
        {
            if (_rebind != null)
            {
                var reset = CreateButton("ResetKeysButton", LEFT_X, BOTTOM_Y, BOTTOM_BUTTON_WIDTH, ROW_HEIGHT,
                    "Reset Keys");
                reset.onClick.AddListener(() =>
                {
                    if (_rebind.IsRebinding) return;

                    _rebind.ResetAll();
                    RefreshKeys();
                });
            }

            var backButton = CreateButton("BackButton", LEFT_X + LEFT_WIDTH - BOTTOM_BUTTON_WIDTH, BOTTOM_Y,
                BOTTOM_BUTTON_WIDTH, ROW_HEIGHT, "Back");
            backButton.onClick.AddListener(() =>
            {
                if (_rebind == null || !_rebind.IsRebinding) back?.Invoke();
            });
        }

        private void StartRebind(InputRebindAdapter.Entry entry, Text label)
        {
            if (_rebind.IsRebinding) return;

            label.text = $"{entry.Label}: Press a key...";
            _rebind.StartRebind(entry, RefreshKeys);
        }

        private void RefreshKeys()
        {
            if (_rebind == null) return;

            for (var i = 0; i < _keyLabels.Count; i++)
            {
                var entry = _rebind.Entries[i];
                _keyLabels[i].text = $"{entry.Label}: {_rebind.GetDisplayName(entry)}";
            }
        }

        /// <summary>
        /// 名前と値を左に、スライダーを右に置いた 1 行を作る。値を動かすと、表示と設定を更新する。
        /// </summary>
        private void CreateSliderRow(int row, string title, float minValue, float maxValue, bool wholeNumbers,
            float current, string format, Action<float> onChanged)
        {
            var y = GetRowY(row);
            var label = UiBuilder.CreateText(_panel, $"{title}Label", LABEL_FONT_SIZE, TextAnchor.MiddleLeft);
            PlaceAtTopLeft(label.rectTransform, LEFT_X, y, LABEL_WIDTH, ROW_HEIGHT);

            var slider = UiBuilder.CreateSlider(_panel, $"{title}Slider", minValue, maxValue, wholeNumbers, _buttonColor,
                _accentColor);
            PlaceAtTopLeft((RectTransform)slider.transform, LEFT_X + LABEL_WIDTH, y, LEFT_WIDTH - LABEL_WIDTH, ROW_HEIGHT);

            label.text = $"{title}: {current.ToString(format)}";
            slider.SetValueWithoutNotify(current);
            slider.onValueChanged.AddListener(value =>
            {
                label.text = $"{title}: {value.ToString(format)}";
                onChanged(value);
            });
        }

        private Button CreateButton(string name, float x, float y, float width, float height, string label)
        {
            var button = UiBuilder.CreateButton(_panel, name, label, BUTTON_FONT_SIZE, _buttonColor);
            PlaceAtTopLeft((RectTransform)button.transform, x, y, width, height);
            return button;
        }
    }
}
