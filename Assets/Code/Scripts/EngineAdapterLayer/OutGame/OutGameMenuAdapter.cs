using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// アウトゲームの画面（ホーム、ステージ選択、スキル装備の仮の画面）を 1 つの Canvas に組み立てて切り替える Adapter。アウトゲームのシーンへ置く。
    /// ステージの表示内容とスキルの枠の表示は Initializer から受け取り、ボタンの操作は Action で返す。どの画面を出しているかはこの Adapter の中だけで持つ。
    /// 画面は uGUI の Canvas を初期化のときに組み立てる。シーンに EventSystem がなければ、ボタンを押せるように作る。
    /// </summary>
    // TODO: スキル装備の画面は区間2で本来の画面に置き換える
    public sealed class OutGameMenuAdapter : InitializableMonoBehaviour
    {
        /// <summary>
        /// ステージ選択に出す、ステージ 1 つ分の表示内容。
        /// </summary>
        public readonly struct StageEntry
        {
            /// <summary> ステージの名前 </summary>
            public readonly string Name;

            /// <summary> 初めてクリアしたときに解放するスキルの名前。解放しなければ空 </summary>
            public readonly string UnlockSkillName;

            /// <summary> クリアしたことがあるか </summary>
            public readonly bool IsCleared;

            /// <summary> 選べるか </summary>
            public readonly bool IsUnlocked;

            public StageEntry(string name, string unlockSkillName, bool isCleared, bool isUnlocked)
            {
                Name = name;
                UnlockSkillName = unlockSkillName;
                IsCleared = isCleared;
                IsUnlocked = isUnlocked;
            }
        }

        private enum MenuScreen
        {
            Home,
            StageSelect,
            SkillEquip,
        }

        private const int SORTING_ORDER = 100;
        private const int TITLE_FONT_SIZE = 72;
        private const int BUTTON_FONT_SIZE = 36;
        private const float BUTTON_GAP = 20f;
        private const float STAGE_BUTTON_HEIGHT = 110f;
        private const float HOME_BUTTON_HEIGHT = 90f;
        private static readonly Vector2 _buttonSize = new(720f, 90f);

        private readonly Dictionary<MenuScreen, GameObject> _panels = new();
        private readonly List<Text> _slotLabels = new();

        [SerializeField]
        [Tooltip("背景の色")]
        private Color _backgroundColor = new(0.05f, 0.05f, 0.08f, 1f);

        [SerializeField]
        [Tooltip("ボタンの色")]
        private Color _buttonColor = new(0.2f, 0.2f, 0.25f, 0.95f);

        private CanvasGroup _canvasGroup;
        private Action<int> _startStage;
        private Action _quit;
        private Action<int> _cycleSlot;

        /// <summary>
        /// 画面を組み立てて、ホームかステージ選択を出す。
        /// </summary>
        /// <param name="stages">ステージ選択に出すステージ。番号はこの並びの位置</param>
        /// <param name="slotLabels">スキル装備に出す、装備枠ごとのスキルの名前</param>
        /// <param name="startStage">ステージのボタンを押したときに呼ぶ処理。引数はステージの番号</param>
        /// <param name="cycleSlot">装備枠のボタンを押したときに呼ぶ処理。引数は枠の番号</param>
        /// <param name="quit">ゲームの終了ボタンを押したときに呼ぶ処理</param>
        /// <param name="startOnStageSelect">ステージ選択から始めるか。false ならホームから始める</param>
        public void Initialize(IReadOnlyList<StageEntry> stages, IReadOnlyList<string> slotLabels,
            Action<int> startStage, Action<int> cycleSlot, Action quit, bool startOnStageSelect)
        {
            _startStage = startStage;
            _cycleSlot = cycleSlot;
            _quit = quit;

            BuildView(stages, slotLabels);
            ShowScreen(startOnStageSelect ? MenuScreen.StageSelect : MenuScreen.Home);
            base.Initialize();
        }

        /// <summary>
        /// 装備枠に出すスキルの名前を替える。
        /// </summary>
        /// <param name="slot">枠の番号（0から）</param>
        /// <param name="label">スキルの名前</param>
        public void SetSlotLabel(int slot, string label)
        {
            if (slot < 0 || slot >= _slotLabels.Count) return;

            _slotLabels[slot].text = GetSlotText(slot, label);
        }

        /// <summary>
        /// ボタンを押せるかを切り替える。ステージへ遷移している間は押せなくする。
        /// </summary>
        public void SetInteractable(bool isInteractable)
        {
            if (_canvasGroup != null) _canvasGroup.interactable = isInteractable;
        }

        private static string GetSlotText(int slot, string label)
        {
            return $"Skill {slot + 1}: {label}";
        }

        private static string GetStageText(StageEntry stage)
        {
            var text = stage.IsCleared ? $"{stage.Name}  [CLEARED]"
                : stage.IsUnlocked ? stage.Name
                : $"{stage.Name}  [LOCKED]";
            if (!string.IsNullOrEmpty(stage.UnlockSkillName)) text += $"\nUnlock: {stage.UnlockSkillName}";
            return text;
        }

        /// <summary>
        /// ボタンを画面の中央から縦に並べる。row は上から数えた位置で、count は並べる数。
        /// </summary>
        private static void PlaceButton(RectTransform buttonRect, int row, int count, float height)
        {
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(_buttonSize.x, height);
            var step = height + BUTTON_GAP;
            buttonRect.anchoredPosition = new Vector2(0f, (count - 1) * 0.5f * step - row * step - 40f);
        }

        /// <summary>
        /// 背景、題、3 つの画面の順に、全画面の Canvas へ組み立てる。
        /// </summary>
        private void BuildView(IReadOnlyList<StageEntry> stages, IReadOnlyList<string> slotLabels)
        {
            var canvas = UiBuilder.CreateCanvas(transform, "OutGameMenu", SORTING_ORDER, true);
            _canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();

            var background = UiBuilder.CreateImage(canvas, "Background", _backgroundColor);
            UiBuilder.SetAnchors(background.rectTransform, Vector2.zero, Vector2.one);

            var title = UiBuilder.CreateText(canvas, "Title", TITLE_FONT_SIZE);
            title.text = "KIZAMI";
            UiBuilder.SetAnchors(title.rectTransform, new Vector2(0f, 0.8f), new Vector2(1f, 0.95f));

            BuildHome(canvas);
            BuildStageSelect(canvas, stages);
            BuildSkillEquip(canvas, slotLabels);

            UiBuilder.EnsureEventSystem(transform);
        }

        private void BuildHome(Transform canvas)
        {
            var panel = CreatePanel(canvas, MenuScreen.Home);

            var stageButton = CreateButton(panel, "StageSelectButton", "Stage Select", 0, 4, HOME_BUTTON_HEIGHT);
            stageButton.onClick.AddListener(() => ShowScreen(MenuScreen.StageSelect));

            var skillButton = CreateButton(panel, "SkillEquipButton", "Skill Equip", 1, 4, HOME_BUTTON_HEIGHT);
            skillButton.onClick.AddListener(() => ShowScreen(MenuScreen.SkillEquip));

            // TODO: コンフィグの画面は区間4で作り、ボタンを押せるようにする
            var configButton = CreateButton(panel, "ConfigButton", "Config", 2, 4, HOME_BUTTON_HEIGHT);
            configButton.interactable = false;

            var quitButton = CreateButton(panel, "QuitButton", "Quit", 3, 4, HOME_BUTTON_HEIGHT);
            quitButton.onClick.AddListener(() => _quit?.Invoke());
        }

        private void BuildStageSelect(Transform canvas, IReadOnlyList<StageEntry> stages)
        {
            var panel = CreatePanel(canvas, MenuScreen.StageSelect);
            var count = stages.Count + 1;

            for (var i = 0; i < stages.Count; i++)
            {
                var stageIndex = i;
                var button = CreateButton(panel, $"Stage{i}Button", GetStageText(stages[i]), i, count,
                    STAGE_BUTTON_HEIGHT);
                button.interactable = stages[i].IsUnlocked;
                button.onClick.AddListener(() => _startStage?.Invoke(stageIndex));
            }

            CreateBackButton(panel, stages.Count, count, STAGE_BUTTON_HEIGHT);
        }

        private void BuildSkillEquip(Transform canvas, IReadOnlyList<string> slotLabels)
        {
            var panel = CreatePanel(canvas, MenuScreen.SkillEquip);
            var count = slotLabels.Count + 1;

            for (var slot = 0; slot < slotLabels.Count; slot++)
            {
                var slotIndex = slot;
                var button = CreateButton(panel, $"Slot{slot}Button", GetSlotText(slot, slotLabels[slot]), slot, count,
                    HOME_BUTTON_HEIGHT);
                _slotLabels.Add(button.GetComponentInChildren<Text>());
                button.onClick.AddListener(() => _cycleSlot?.Invoke(slotIndex));
            }

            CreateBackButton(panel, slotLabels.Count, count, HOME_BUTTON_HEIGHT);
        }

        private Transform CreatePanel(Transform canvas, MenuScreen screen)
        {
            var panel = new GameObject(screen.ToString(), typeof(RectTransform));
            panel.transform.SetParent(canvas, false);
            UiBuilder.SetAnchors((RectTransform)panel.transform, Vector2.zero, Vector2.one);
            _panels.Add(screen, panel);
            return panel.transform;
        }

        private Button CreateButton(Transform panel, string name, string label, int row, int count, float height)
        {
            var button = UiBuilder.CreateButton(panel, name, label, BUTTON_FONT_SIZE, _buttonColor);
            PlaceButton((RectTransform)button.transform, row, count, height);
            return button;
        }

        private void CreateBackButton(Transform panel, int row, int count, float height)
        {
            var button = CreateButton(panel, "BackButton", "Back", row, count, height);
            button.onClick.AddListener(() => ShowScreen(MenuScreen.Home));
        }

        private void ShowScreen(MenuScreen screen)
        {
            foreach (var pair in _panels)
            {
                pair.Value.SetActive(pair.Key == screen);
            }
        }
    }
}
