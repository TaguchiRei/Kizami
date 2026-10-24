using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
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

        /// <summary>
        /// スキル装備に出す、スキル 1 つ分の表示内容。
        /// </summary>
        public readonly struct SkillEntry
        {
            /// <summary> スキルの名前 </summary>
            public readonly string Name;

            /// <summary> 発動に使うチャージ量 </summary>
            public readonly int Cost;

            /// <summary> 説明文。なければ空 </summary>
            public readonly string Description;

            /// <summary> 解放済みか </summary>
            public readonly bool IsUnlocked;

            public SkillEntry(string name, int cost, string description, bool isUnlocked)
            {
                Name = name;
                Cost = cost;
                Description = description;
                IsUnlocked = isUnlocked;
            }
        }

        private enum MenuScreen
        {
            Home,
            StageSelect,
            SkillEquip,
            Config,
        }

        private const int SORTING_ORDER = 100;
        private const int TITLE_FONT_SIZE = 72;
        private const int BUTTON_FONT_SIZE = 36;
        private const float BUTTON_GAP = 20f;
        private const float STAGE_BUTTON_HEIGHT = 110f;
        private const float HOME_BUTTON_HEIGHT = 90f;
        private const int NO_SKILL = -1;
        private const float TOAST_SECONDS = 3f;
        private const int DETAIL_FONT_SIZE = 28;
        private const float SLOT_X = 100f;
        private const float SLOT_WIDTH = 460f;
        private const float SLOT_HEIGHT = 90f;
        private const float SKILL_X = 640f;
        private const float SKILL_WIDTH = 780f;
        private const float SKILL_HEIGHT = 70f;
        private const float ULTIMATE_HEIGHT = 110f;
        private const float ROW_GAP = 10f;
        private static readonly Vector2 _buttonSize = new(720f, 90f);

        private readonly Dictionary<MenuScreen, GameObject> _panels = new();
        private readonly List<Button> _slotButtons = new();
        private readonly List<Text> _slotLabels = new();
        private readonly List<Button> _skillButtons = new();
        private readonly List<Text> _skillLabels = new();
        private readonly Queue<(string SkillName, bool IsUltimate)> _notices = new();

        [SerializeField]
        [Tooltip("背景の色")]
        private Color _backgroundColor = new(0.05f, 0.05f, 0.08f, 1f);

        [SerializeField]
        [Tooltip("ボタンの色")]
        private Color _buttonColor = new(0.2f, 0.2f, 0.25f, 0.95f);

        [SerializeField]
        [Tooltip("選んでいる装備枠のボタンの色")]
        private Color _selectedColor = new(0.35f, 0.45f, 0.7f, 0.95f);

        [SerializeField]
        [Tooltip("専用の枠のスキルのボタンの色")]
        private Color _ultimateColor = new(0.6f, 0.45f, 0.1f, 0.95f);

        private CanvasGroup _canvasGroup;
        private Action<int> _startStage;
        private Action<int, int> _equipSkill;
        private IOperationSettingState _settingState;
        private IOperationSettingController _settingController;
        private InputRebindAdapter _rebindAdapter;
        private Text _title;
        private GameObject _toast;
        private Text _toastText;
        private GameObject _overlay;
        private Text _overlaySkillText;
        private float _toastTimer;
        private Text _detailText;
        private IReadOnlyList<SkillEntry> _skills;
        private int[] _equippedSkillIndices;
        private int _selectedSlot;

        /// <summary>
        /// コンフィグの画面に渡すものを設定する。Initialize より前に呼ぶ。呼ばなければ、ホームのコンフィグのボタンは押せない。
        /// </summary>
        /// <param name="settingState">設定の値の取得元</param>
        /// <param name="settingController">設定を変える窓口</param>
        /// <param name="rebindAdapter">キーの割り当てを変える Adapter。なければキーの割り当ては出さない</param>
        public void SetConfig(IOperationSettingState settingState, IOperationSettingController settingController,
            InputRebindAdapter rebindAdapter)
        {
            _settingState = settingState;
            _settingController = settingController;
            _rebindAdapter = rebindAdapter;
        }

        /// <summary>
        /// 画面を組み立てて、ホームかステージ選択を出す。
        /// </summary>
        /// <param name="stages">ステージ選択に出すステージ。番号はこの並びの位置</param>
        /// <param name="skills">スキル装備に出すスキル。番号はこの並びの位置</param>
        /// <param name="ultimateSkillIndex">専用の枠に置くスキルの番号。なければ -1</param>
        /// <param name="equippedSkillIndices">装備枠ごとのスキルの番号。空きは -1</param>
        /// <param name="startStage">ステージのボタンを押したときに呼ぶ処理。引数はステージの番号</param>
        /// <param name="equipSkill">スキルのボタンを押したときに呼ぶ処理。引数は枠の番号とスキルの番号（空きにするなら -1）</param>
        /// <param name="startOnStageSelect">ステージ選択から始めるか。false ならホームから始める</param>
        public void Initialize(IReadOnlyList<StageEntry> stages, IReadOnlyList<SkillEntry> skills,
            int ultimateSkillIndex, IReadOnlyList<int> equippedSkillIndices, Action<int> startStage,
            Action<int, int> equipSkill, bool startOnStageSelect)
        {
            _startStage = startStage;
            _equipSkill = equipSkill;
            _skills = skills;
            _equippedSkillIndices = new int[equippedSkillIndices.Count];

            BuildView(stages, ultimateSkillIndex);
            SetEquipped(equippedSkillIndices);
            ShowScreen(startOnStageSelect ? MenuScreen.StageSelect : MenuScreen.Home);
            base.Initialize();
        }

        /// <summary>
        /// 装備枠ごとのスキルの番号を受け取り、枠とスキルの表示を更新する。
        /// </summary>
        /// <param name="equippedSkillIndices">装備枠ごとのスキルの番号。空きは -1</param>
        public void SetEquipped(IReadOnlyList<int> equippedSkillIndices)
        {
            for (var slot = 0; slot < _equippedSkillIndices.Length && slot < equippedSkillIndices.Count; slot++)
            {
                _equippedSkillIndices[slot] = equippedSkillIndices[slot];
            }

            RefreshSkillEquip();
        }

        /// <summary>
        /// 解放の演出を順番待ちに加える。通常のスキルは画面の端に小さな通知を数秒出し、専用の枠のスキルは全画面で見せて、ボタンを押すまで閉じない。
        /// </summary>
        /// <param name="skillName">解放されたスキルの名前</param>
        /// <param name="isUltimate">専用の枠のスキルか</param>
        public void QueueUnlockNotice(string skillName, bool isUltimate)
        {
            _notices.Enqueue((skillName, isUltimate));
        }

        /// <summary>
        /// ボタンを押せるかを切り替える。ステージへ遷移している間は押せなくする。
        /// </summary>
        public void SetInteractable(bool isInteractable)
        {
            if (_canvasGroup != null) _canvasGroup.interactable = isInteractable;
        }

        private void Update()
        {
            if (!Initialized) return;

            if (_toast.activeSelf)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                if (_toastTimer <= 0f) _toast.SetActive(false);
                return;
            }

            if (_overlay.activeSelf || _notices.Count == 0) return;

            var notice = _notices.Dequeue();
            if (notice.IsUltimate)
            {
                _overlaySkillText.text = notice.SkillName;
                _overlay.SetActive(true);
            }
            else
            {
                _toastText.text = $"Skill unlocked: {notice.SkillName}";
                _toastTimer = TOAST_SECONDS;
                _toast.SetActive(true);
            }
        }

        /// <summary>
        /// ゲームを終了する。エディタではプレイモードを止める。
        /// </summary>
        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }

        private static string GetSlotText(int slot, string skillName)
        {
            return $"Slot {slot + 1}: {skillName}";
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
        private void BuildView(IReadOnlyList<StageEntry> stages, int ultimateSkillIndex)
        {
            var canvas = UiBuilder.CreateCanvas(transform, "OutGameMenu", SORTING_ORDER, true);
            _canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();

            var background = UiBuilder.CreateImage(canvas, "Background", _backgroundColor);
            UiBuilder.SetAnchors(background.rectTransform, Vector2.zero, Vector2.one);

            _title = UiBuilder.CreateText(canvas, "Title", TITLE_FONT_SIZE);
            _title.text = "KIZAMI";
            UiBuilder.SetAnchors(_title.rectTransform, new Vector2(0f, 0.8f), new Vector2(1f, 0.95f));

            BuildHome(canvas);
            BuildStageSelect(canvas, stages);
            BuildSkillEquip(canvas, ultimateSkillIndex);
            BuildConfig(canvas);

            BuildNotices(canvas);
            UiBuilder.EnsureEventSystem(transform);
        }

        /// <summary>
        /// 解放の演出の部品を、ほかの画面より手前に組み立てて非表示にしておく。
        /// </summary>
        private void BuildNotices(Transform canvas)
        {
            var toast = UiBuilder.CreateImage(canvas, "UnlockToast", _selectedColor);
            UiBuilder.SetAnchors(toast.rectTransform, new Vector2(0.62f, 0.9f), new Vector2(0.98f, 0.98f));
            _toast = toast.gameObject;
            _toastText = UiBuilder.CreateText(toast.transform, "Label", DETAIL_FONT_SIZE);
            UiBuilder.SetAnchors(_toastText.rectTransform, Vector2.zero, Vector2.one);
            _toast.SetActive(false);

            var overlay = UiBuilder.CreateImage(canvas, "UltimateUnlockOverlay", new Color(0f, 0f, 0f, 0.9f));
            overlay.raycastTarget = true;
            UiBuilder.SetAnchors(overlay.rectTransform, Vector2.zero, Vector2.one);
            _overlay = overlay.gameObject;

            var heading = UiBuilder.CreateText(overlay.transform, "Heading", BUTTON_FONT_SIZE);
            heading.text = "ULTIMATE SKILL UNLOCKED";
            heading.color = _ultimateColor + new Color(0.3f, 0.3f, 0.3f, 0f);
            UiBuilder.SetAnchors(heading.rectTransform, new Vector2(0f, 0.6f), new Vector2(1f, 0.7f));

            _overlaySkillText = UiBuilder.CreateText(overlay.transform, "SkillName", TITLE_FONT_SIZE);
            UiBuilder.SetAnchors(_overlaySkillText.rectTransform, new Vector2(0f, 0.42f), new Vector2(1f, 0.58f));

            var okButton = UiBuilder.CreateButton(overlay.transform, "OkButton", "OK", BUTTON_FONT_SIZE, _ultimateColor);
            var okRect = (RectTransform)okButton.transform;
            okRect.anchorMin = okRect.anchorMax = new Vector2(0.5f, 0.28f);
            okRect.sizeDelta = new Vector2(320f, SLOT_HEIGHT);
            okRect.anchoredPosition = Vector2.zero;
            okButton.onClick.AddListener(() => _overlay.SetActive(false));
            _overlay.SetActive(false);
        }

        private void BuildHome(Transform canvas)
        {
            var panel = CreatePanel(canvas, MenuScreen.Home);

            var stageButton = CreateButton(panel, "StageSelectButton", "Stage Select", 0, 4, HOME_BUTTON_HEIGHT);
            stageButton.onClick.AddListener(() => ShowScreen(MenuScreen.StageSelect));

            var skillButton = CreateButton(panel, "SkillEquipButton", "Skill Equip", 1, 4, HOME_BUTTON_HEIGHT);
            skillButton.onClick.AddListener(() => ShowScreen(MenuScreen.SkillEquip));

            var configButton = CreateButton(panel, "ConfigButton", "Config", 2, 4, HOME_BUTTON_HEIGHT);
            configButton.interactable = _settingState != null && _settingController != null;
            configButton.onClick.AddListener(() => ShowScreen(MenuScreen.Config));

            var quitButton = CreateButton(panel, "QuitButton", "Quit", 3, 4, HOME_BUTTON_HEIGHT);
            quitButton.onClick.AddListener(Quit);
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

        private void BuildSkillEquip(Transform canvas, int ultimateSkillIndex)
        {
            var panel = CreatePanel(canvas, MenuScreen.SkillEquip);

            for (var slot = 0; slot < _equippedSkillIndices.Length; slot++)
            {
                var slotIndex = slot;
                var button = CreateButtonAt(panel, $"Slot{slot}Button", string.Empty, SLOT_X, 60f + slot * (SLOT_HEIGHT + ROW_GAP),
                    SLOT_WIDTH, SLOT_HEIGHT, _buttonColor);
                _slotButtons.Add(button);
                _slotLabels.Add(button.GetComponentInChildren<Text>());
                button.onClick.AddListener(() => SelectSlot(slotIndex));
            }

            var unequipY = 60f + _equippedSkillIndices.Length * (SLOT_HEIGHT + ROW_GAP);
            var unequipButton = CreateButtonAt(panel, "UnequipButton", "Unequip", SLOT_X, unequipY, SLOT_WIDTH, SLOT_HEIGHT * 0.8f,
                _buttonColor);
            unequipButton.onClick.AddListener(() => _equipSkill?.Invoke(_selectedSlot, NO_SKILL));

            _detailText = UiBuilder.CreateText(panel, "Detail", DETAIL_FONT_SIZE, TextAnchor.UpperLeft);
            _detailText.horizontalOverflow = HorizontalWrapMode.Wrap;
            PlaceAtTopLeft(_detailText.rectTransform, SLOT_X, unequipY + SLOT_HEIGHT + 40f, SLOT_WIDTH, 360f);

            var listTop = ultimateSkillIndex >= 0 ? 60f + ULTIMATE_HEIGHT + ROW_GAP * 2f : 60f;
            for (var i = 0; i < _skills.Count; i++)
            {
                var skillIndex = i;
                var isUltimate = i == ultimateSkillIndex;
                var y = isUltimate ? 60f : listTop;
                var button = CreateButtonAt(panel, $"Skill{i}Button", string.Empty, SKILL_X, y, SKILL_WIDTH,
                    isUltimate ? ULTIMATE_HEIGHT : SKILL_HEIGHT, isUltimate ? _ultimateColor : _buttonColor);
                button.onClick.AddListener(() => PickSkill(skillIndex));
                _skillButtons.Add(button);
                _skillLabels.Add(button.GetComponentInChildren<Text>());

                if (!isUltimate) listTop += SKILL_HEIGHT + ROW_GAP;
            }

            var back = CreateButtonAt(panel, "BackButton", "Back", SLOT_X, 940f, SLOT_WIDTH, SLOT_HEIGHT * 0.8f, _buttonColor);
            back.onClick.AddListener(() => ShowScreen(MenuScreen.Home));
        }

        private void BuildConfig(Transform canvas)
        {
            var panel = CreatePanel(canvas, MenuScreen.Config);
            if (_settingState == null || _settingController == null) return;

            _ = new ConfigScreen(panel, _settingState, _settingController, _rebindAdapter, _buttonColor, _selectedColor,
                () => ShowScreen(MenuScreen.Home));
        }

        private void SelectSlot(int slot)
        {
            _selectedSlot = slot;
            RefreshSkillEquip();
            ShowDetail(_equippedSkillIndices[slot]);
        }

        private void PickSkill(int skillIndex)
        {
            _equipSkill?.Invoke(_selectedSlot, skillIndex);
            ShowDetail(skillIndex);
        }

        private void ShowDetail(int skillIndex)
        {
            if (skillIndex < 0 || skillIndex >= _skills.Count)
            {
                _detailText.text = string.Empty;
                return;
            }

            var skill = _skills[skillIndex];
            _detailText.text = $"{skill.Name}\nCost: {skill.Cost}" +
                               (string.IsNullOrEmpty(skill.Description) ? string.Empty : $"\n\n{skill.Description}");
        }

        /// <summary>
        /// 装備枠のボタンの文字と選択の色、スキルのボタンの文字（ロック中の印と、装備している枠）を更新する。
        /// </summary>
        private void RefreshSkillEquip()
        {
            for (var slot = 0; slot < _slotLabels.Count; slot++)
            {
                var index = _equippedSkillIndices[slot];
                var skillName = index >= 0 && index < _skills.Count ? _skills[index].Name : "(empty)";
                _slotLabels[slot].text = GetSlotText(slot, skillName);
                _slotButtons[slot].GetComponent<Image>().color = slot == _selectedSlot ? _selectedColor : _buttonColor;
            }

            for (var i = 0; i < _skillLabels.Count; i++)
            {
                var skill = _skills[i];
                var text = skill.IsUnlocked ? skill.Name : $"{skill.Name}  [LOCKED]";
                var equippedSlot = Array.IndexOf(_equippedSkillIndices, i);
                if (equippedSlot >= 0) text += $"  [Slot {equippedSlot + 1}]";
                _skillLabels[i].text = text;
                _skillButtons[i].interactable = skill.IsUnlocked;
            }
        }

        /// <summary>
        /// 基準の解像度の左上からの位置と大きさを指定して、ボタンを置く。
        /// </summary>
        private Button CreateButtonAt(Transform panel, string name, string label, float x, float y, float width,
            float height, Color color)
        {
            var button = UiBuilder.CreateButton(panel, name, label, BUTTON_FONT_SIZE, color);
            PlaceAtTopLeft((RectTransform)button.transform, x, y, width, height);
            return button;
        }

        private static void PlaceAtTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
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

            _title.gameObject.SetActive(screen == MenuScreen.Home || screen == MenuScreen.StageSelect);
        }
    }
}
