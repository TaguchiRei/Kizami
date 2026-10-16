using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.UI;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// インゲームの HUD（HP とチャージのゲージ、スキルの枠、破壊の進み具合と倒した敵の数、制限時間の残り、ランチャーの装填状態、開始の文字）を出す Adapter。インゲームのシーンへ置く。
    /// 画面は uGUI の Canvas を初期化のときに組み立てる。値は State から読み、ランチャーの装填状態だけは State がない為、FragmentThrowAdapter から読む。
    /// </summary>
    public sealed class StageHudAdapter : InitializableMonoBehaviour
    {
        private const int SORTING_ORDER = 10;
        private const int LABEL_FONT_SIZE = 28;
        private const int SLOT_FONT_SIZE = 22;
        private const int CHARGE_FONT_SIZE = 16;
        private const int START_FONT_SIZE = 72;
        private const string START_TEXT = "START";
        private const float SLOT_WIDTH = 0.06f;
        private const float SLOT_GAP = 0.01f;
        private const float LINE_OVERHANG = 0.3f;
        /// <summary> 枠の中でアイコンが占める横の割合。残りに文字を出す </summary>
        private const float ICON_WIDTH_RATIO = 0.55f;

        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("ゲージの横幅（画面の横幅に対する割合）")]
        private float _gaugeWidthRatio = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("開始の文字を出しておく時間（秒、実時間）")]
        private float _startDisplayDuration = 1f;

        [SerializeField, Min(1f)]
        [Tooltip("発動できるようになる線の太さ（基準の解像度での px）")]
        private float _lineWidth = 2f;

        [SerializeField]
        [Tooltip("ゲージの減った部分の色")]
        private Color _backgroundColor = new(0f, 0f, 0f, 0.5f);

        [SerializeField]
        [Tooltip("HP のゲージの色")]
        private Color _healthColor = new(0.9f, 0.25f, 0.25f);

        [SerializeField]
        [Tooltip("チャージのゲージの色")]
        private Color _chargeColor = new(0.3f, 0.8f, 1f);

        [SerializeField]
        [Tooltip("チャージが消費量に届いているスキルの線と枠の色")]
        private Color _reachedColor = Color.white;

        [SerializeField]
        [Tooltip("チャージが消費量に届いていないスキルの線と枠の色")]
        private Color _chargingColor = new(1f, 1f, 1f, 0.35f);

        [SerializeField]
        [Tooltip("アイコンがないスキルの枠の色")]
        private Color _emptyIconColor = new(1f, 1f, 1f, 0.25f);

        [SerializeField]
        [Tooltip("チャージ量の数値と、発動できるスキルの「可」の文字の色")]
        private Color _textColor = Color.white;

        [SerializeField]
        [Tooltip("発動できないスキルの「不」の文字の色")]
        private Color _unavailableTextColor = new(0.5f, 0.5f, 0.5f);

        [SerializeField, Min(0f)]
        [Tooltip("残り時間がこの秒数以下になったら、警告の色にする")]
        private float _timeWarningSeconds = 30f;

        [SerializeField]
        [Tooltip("残り時間が少ないときの文字の色")]
        private Color _timeWarningColor = new(0.95f, 0.25f, 0.25f);

        private IPlayerHealthState _healthState;
        private IChargeState _chargeState;
        private ISkillSlotState _slotState;
        private IStageFlowState _flowState;
        private FragmentThrowAdapter _fragmentThrowAdapter;

        private RectTransform _healthFill;
        private RectTransform _chargeFill;
        /// <summary> 装備枠ごとの左右の線。枠の番号 × 2 ＋ 左右（0が左） </summary>
        private Image[] _skillLines;
        private GameObject[] _slots;
        private Text[] _slotCostTexts;
        private Text[] _slotStatusTexts;
        private Text _chargeValueText;
        private Text _targetText;
        private Text _defeatText;
        private Text _timeText;
        private Text _launcherText;
        private Text _startText;
        private float _startHideTime = -1f;

        /// <summary>
        /// 中央から左右へ、割合の分だけ伸ばした範囲にする。
        /// </summary>
        private static void SetCenteredRatio(RectTransform rect, float ratio)
        {
            var half = Mathf.Clamp01(ratio) * 0.5f;
            UiBuilder.SetAnchors(rect, new Vector2(0.5f - half, 0f), new Vector2(0.5f + half, 1f));
        }

        /// <summary>
        /// 必要な State を取得し、HUD を組み立てる。HP・チャージ・装備枠・流れの State の登録より後に呼ぶ。
        /// </summary>
        /// <param name="blackBoard">HP・チャージ・装備枠・流れの State の取得元</param>
        /// <param name="fragmentThrowAdapter">ランチャーの装填状態の取得元</param>
        public void Initialize(IBlackBoard blackBoard, FragmentThrowAdapter fragmentThrowAdapter)
        {
            if (!blackBoard.TryGetSceneState<PlayerBoard, IPlayerHealthState>(out _healthState, this) ||
                !blackBoard.TryGetSceneState<PlayerBoard, IChargeState>(out _chargeState, this) ||
                !blackBoard.TryGetSceneState<PlayerBoard, ISkillSlotState>(out _slotState, this) ||
                !blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this)) return;

            _fragmentThrowAdapter = fragmentThrowAdapter;
            BuildView();
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized) return;

            SetCenteredRatio(_healthFill, (float)_healthState.Current / _healthState.Max);
            SetCenteredRatio(_chargeFill, (float)_chargeState.Current / _chargeState.Max);
            _chargeValueText.text = _chargeState.Current.ToString();
            UpdateSkills();

            _targetText.text = $"Targets {_flowState.DestroyedTargetCount} / {_flowState.TargetCount}";
            _defeatText.text = $"Defeated {_flowState.DefeatCount}";
            UpdateTimeText();
            _launcherText.text = _fragmentThrowAdapter != null && _fragmentThrowAdapter.IsLoaded
                ? "Launcher: LOADED"
                : "Launcher: EMPTY";

            UpdateStartText();
        }

        /// <summary>
        /// 装備しているスキルごとに、ゲージが消費量まで溜まったときの両端の位置へ線を置き、枠に発動できるかを出す。
        /// 空きの枠と、上限を超える消費量の線は隠す。
        /// </summary>
        private void UpdateSkills()
        {
            for (var slot = 0; slot < _slotState.SlotCount; slot++)
            {
                var isEquipped = _slotState.TryGetCost(slot, out var cost);
                var isReached = isEquipped && _chargeState.Current >= cost;
                var color = isReached ? _reachedColor : _chargingColor;
                _slots[slot].SetActive(isEquipped);
                if (isEquipped)
                {
                    _slotStatusTexts[slot].text = isReached ? "可" : "不";
                    _slotStatusTexts[slot].color = isReached ? _textColor : _unavailableTextColor;
                    _slotCostTexts[slot].enabled = !isReached;
                    _slotCostTexts[slot].text = cost.ToString();
                }

                var hasLine = isEquipped && cost <= _chargeState.Max;
                var half = hasLine ? 0.5f * cost / _chargeState.Max : 0f;
                for (var side = 0; side < 2; side++)
                {
                    var line = _skillLines[slot * 2 + side];
                    line.enabled = hasLine;
                    if (!hasLine) continue;

                    line.color = color;
                    SetLine(line.rectTransform, side == 0 ? 0.5f - half : 0.5f + half);
                }
            }
        }

        /// <summary>
        /// 制限時間の残りを「分:秒」で出す。残りが警告の秒数以下なら警告の色にする。制限時間がないステージでは隠す。
        /// </summary>
        private void UpdateTimeText()
        {
            var remaining = _flowState.RemainingTime;
            _timeText.enabled = remaining >= 0f;
            if (!_timeText.enabled) return;

            var seconds = Mathf.CeilToInt(remaining);
            _timeText.text = $"{seconds / 60:00}:{seconds % 60:00}";
            _timeText.color = remaining <= _timeWarningSeconds ? _timeWarningColor : _textColor;
        }

        /// <summary>
        /// プレイ中になったら開始の文字を出し、決めた時間が経ったら隠す。時間を止めても進むように実時間で数える。
        /// </summary>
        private void UpdateStartText()
        {
            if (_startHideTime < 0f && _flowState.Phase == StagePhase.Playing)
            {
                _startHideTime = Time.unscaledTime + _startDisplayDuration;
                _startText.enabled = true;
            }

            if (_startText.enabled && Time.unscaledTime >= _startHideTime) _startText.enabled = false;
        }

        /// <summary>
        /// 画面に重ねる Canvas に、ゲージ、スキルの枠、文字を組み立てる。
        /// </summary>
        private void BuildView()
        {
            var canvas = UiBuilder.CreateCanvas(transform, "StageHud", SORTING_ORDER, false);
            var halfWidth = _gaugeWidthRatio * 0.5f;

            _healthFill = CreateGauge(canvas, "Health", new Vector2(0.5f - halfWidth, 0.03f),
                new Vector2(0.5f + halfWidth, 0.045f), _healthColor);
            _chargeFill = CreateGauge(canvas, "Charge", new Vector2(0.5f - halfWidth, 0.055f),
                new Vector2(0.5f + halfWidth, 0.065f), _chargeColor);

            _chargeValueText = UiBuilder.CreateText(_chargeFill.parent, "Value", CHARGE_FONT_SIZE);
            UiBuilder.SetAnchors(_chargeValueText.rectTransform, Vector2.zero, Vector2.one);
            _chargeValueText.color = _textColor;

            // 線は、チャージのゲージの全体（減った部分の背景）に対する割合で置く
            var chargeBackground = _chargeFill.parent;
            _skillLines = new Image[_slotState.SlotCount * 2];
            _slots = new GameObject[_slotState.SlotCount];
            _slotCostTexts = new Text[_slotState.SlotCount];
            _slotStatusTexts = new Text[_slotState.SlotCount];
            var slotsLeft = 0.5f - (_slotState.SlotCount * SLOT_WIDTH + (_slotState.SlotCount - 1) * SLOT_GAP) * 0.5f;
            for (var slot = 0; slot < _slotState.SlotCount; slot++)
            {
                _skillLines[slot * 2] = UiBuilder.CreateImage(chargeBackground, $"SkillLine{slot + 1}L", _chargingColor);
                _skillLines[slot * 2 + 1] = UiBuilder.CreateImage(chargeBackground, $"SkillLine{slot + 1}R", _chargingColor);

                var left = slotsLeft + slot * (SLOT_WIDTH + SLOT_GAP);
                BuildSlot(canvas, slot, new Vector2(left, 0.075f), new Vector2(left + SLOT_WIDTH, 0.125f));
            }

            _targetText = UiBuilder.CreateText(canvas, "Targets", LABEL_FONT_SIZE);
            UiBuilder.SetAnchors(_targetText.rectTransform, new Vector2(0.3f, 0.92f), new Vector2(0.7f, 0.98f));

            _defeatText = UiBuilder.CreateText(canvas, "Defeated", LABEL_FONT_SIZE, TextAnchor.MiddleRight);
            UiBuilder.SetAnchors(_defeatText.rectTransform, new Vector2(0.7f, 0.92f), new Vector2(0.98f, 0.98f));

            _timeText = UiBuilder.CreateText(canvas, "Time", LABEL_FONT_SIZE);
            UiBuilder.SetAnchors(_timeText.rectTransform, new Vector2(0.4f, 0.86f), new Vector2(0.6f, 0.92f));
            _timeText.enabled = false;

            _launcherText = UiBuilder.CreateText(canvas, "Launcher", LABEL_FONT_SIZE, TextAnchor.MiddleRight);
            UiBuilder.SetAnchors(_launcherText.rectTransform, new Vector2(0.7f, 0.03f), new Vector2(0.98f, 0.09f));

            _startText = UiBuilder.CreateText(canvas, "Start", START_FONT_SIZE);
            UiBuilder.SetAnchors(_startText.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 0.7f));
            _startText.text = START_TEXT;
            _startText.enabled = false;
        }

        /// <summary>
        /// スキルの枠を、左にアイコン（アイコンがなければ四角）、右の上に必要なチャージ量、右の下に発動できるかの文字で組み立てる。
        /// </summary>
        private void BuildSlot(RectTransform canvas, int slot, Vector2 anchorMin, Vector2 anchorMax)
        {
            var root = new GameObject($"SkillSlot{slot + 1}", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(canvas, false);
            UiBuilder.SetAnchors(rootRect, anchorMin, anchorMax);
            _slots[slot] = root;

            var icon = _slotState.GetIcon(slot);
            if (icon != null)
            {
                var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
                iconObject.transform.SetParent(rootRect, false);
                var rawImage = iconObject.GetComponent<RawImage>();
                rawImage.texture = icon;
                rawImage.raycastTarget = false;
                UiBuilder.SetAnchors(rawImage.rectTransform, Vector2.zero, new Vector2(ICON_WIDTH_RATIO, 1f));
            }
            else
            {
                var square = UiBuilder.CreateImage(rootRect, "Icon", _emptyIconColor);
                UiBuilder.SetAnchors(square.rectTransform, Vector2.zero, new Vector2(ICON_WIDTH_RATIO, 1f));
            }

            _slotCostTexts[slot] = UiBuilder.CreateText(rootRect, "Cost", SLOT_FONT_SIZE);
            UiBuilder.SetAnchors(_slotCostTexts[slot].rectTransform, new Vector2(ICON_WIDTH_RATIO, 0.5f), Vector2.one);
            _slotCostTexts[slot].color = _textColor;

            _slotStatusTexts[slot] = UiBuilder.CreateText(rootRect, "Status", SLOT_FONT_SIZE);
            UiBuilder.SetAnchors(_slotStatusTexts[slot].rectTransform, new Vector2(ICON_WIDTH_RATIO, 0f), new Vector2(1f, 0.5f));
        }

        /// <summary>
        /// 減った部分の色の背景と、その中で中央から伸び縮みする量の部分を作り、量の部分を返す。
        /// </summary>
        private RectTransform CreateGauge(RectTransform canvas, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var background = UiBuilder.CreateImage(canvas, name, _backgroundColor);
            UiBuilder.SetAnchors(background.rectTransform, anchorMin, anchorMax);

            var fill = UiBuilder.CreateImage(background.transform, "Fill", color);
            SetCenteredRatio(fill.rectTransform, 1f);
            return fill.rectTransform;
        }

        /// <summary>
        /// 縦線を、親に対する横の割合の位置へ、太さを保って置く。縦はゲージの上下へはみ出させる。
        /// </summary>
        private void SetLine(RectTransform line, float x)
        {
            line.anchorMin = new Vector2(x, -LINE_OVERHANG);
            line.anchorMax = new Vector2(x, 1f + LINE_OVERHANG);
            line.offsetMin = new Vector2(-_lineWidth * 0.5f, 0f);
            line.offsetMax = new Vector2(_lineWidth * 0.5f, 0f);
        }
    }
}
