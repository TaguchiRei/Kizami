using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.UI;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// インゲームの HUD（HP とチャージのゲージ、スキルの枠、破壊の進み具合、倒した敵の数、制限時間の残り、体力制の破壊対象の名前と体力のゲージ、ランチャーの装填状態、開始の文字）を出す Adapter。インゲームのシーンへ置く。
    /// 画面は uGUI の Canvas を初期化のときに組み立てる。値は State から読み、ランチャーの装填状態だけは State がない為、FragmentThrowAdapter から読む。
    /// </summary>
    /// <remarks>
    /// 表示した値を覚えておき、値が変わった部分だけを書き直す。毎フレームの文字列の生成と、uGUI の作り直しを避ける為。
    /// </remarks>
    public sealed class StageHudAdapter : InitializableMonoBehaviour
    {
        private const int SORTING_ORDER = 10;
        private const int LABEL_FONT_SIZE = 28;
        private const int SLOT_FONT_SIZE = 22;
        private const int CHARGE_FONT_SIZE = 16;

        /// <summary> チャージのゲージの下端と上端の高さ（画面の割合） </summary>
        private const float CHARGE_GAUGE_MIN_Y = 0.055f;

        private const float CHARGE_GAUGE_MAX_Y = 0.065f;

        /// <summary> チャージの数値の後ろに置くひし形の一辺（基準の解像度での px） </summary>
        private const float DIAMOND_SIZE = 40f;
        private const int START_FONT_SIZE = 72;
        private const string START_TEXT = "START";
        private const string FLIGHT_PROMPT_TEXT = "[F] JUMP";
        private const float SLOT_WIDTH = 0.06f;
        private const float SLOT_GAP = 0.01f;
        private const float LINE_OVERHANG = 0.3f;
        /// <summary> 枠の中でアイコンが占める横の割合。残りに文字を出す </summary>
        private const float ICON_WIDTH_RATIO = 0.55f;
        /// <summary> 次のフレームで必ず書き直すように、表示した値へ入れておく値 </summary>
        private const int REDRAW_REQUIRED = int.MinValue;
        /// <summary> 装備していない枠の、表示した消費量 </summary>
        private const int UNEQUIPPED_COST = -1;

        /// <summary> ゲージのひびの折れ目。ゲージの背景に対する割合の位置で、上から下へ順に線で継ぐ </summary>
        private static readonly Vector2[] _crackPoints =
        {
            new(0.52f, 1.35f), new(0.47f, 0.8f), new(0.54f, 0.45f), new(0.48f, 0.1f), new(0.51f, -0.35f),
        };

        /// <summary> ゲージのひびの枝。始点と終点の組を並べる </summary>
        private static readonly Vector2[] _crackBranches =
        {
            new(0.47f, 0.8f), new(0.41f, 1.0f),
            new(0.54f, 0.45f), new(0.61f, 0.25f),
            new(0.48f, 0.1f), new(0.44f, -0.15f),
        };

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
        [Tooltip("チャージの数値の後ろのひし形の色")]
        private Color _diamondColor = new(0.3f, 0.8f, 1f, 0.9f);

        [SerializeField, Min(1f)]
        [Tooltip("チャージが増えた瞬間に、ひし形が一瞬で広がる大きさの倍率")]
        private float _diamondPulseScale = 1.7f;

        [SerializeField, Min(0.01f)]
        [Tooltip("ひし形が元の大きさへ戻るまでの時間（秒）")]
        private float _diamondPulseDuration = 0.25f;

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

        [SerializeField]
        [Tooltip("体力制の破壊対象のゲージの色")]
        private Color _targetHealthColor = new(0.95f, 0.6f, 0.2f);

        [SerializeField]
        [Tooltip("無敵の間の、体力制の破壊対象のゲージの色")]
        private Color _invincibleColor = new(0.6f, 0.6f, 0.6f);

        [SerializeField]
        [Tooltip("壊れて止まった対象のゲージに入れる、ひびの色")]
        private Color _crackColor = new(0.08f, 0.08f, 0.08f, 1f);

        [SerializeField, Min(0.5f)]
        [Tooltip("ひびの線の太さ（px）")]
        private float _crackWidth = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("ひびで割れたゲージの左右を、それぞれ傾ける角度（度）")]
        private float _brokenTilt = 2.5f;

        [SerializeField, Min(0f)]
        [Tooltip("ひびで割れたゲージの左右を、外へずらす量（px）")]
        private float _brokenGap = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("ひびが入った瞬間に、ゲージを揺らす時間（秒）")]
        private float _brokenShakeDuration = 0.2f;

        [SerializeField, Min(0f)]
        [Tooltip("ひびが入った瞬間に、ゲージを揺らす幅（px）")]
        private float _brokenShakeStrength = 6f;

        [SerializeField]
        [Tooltip("時間で減っていく間に、灰色のゲージを点滅させる色")]
        private Color _drainingFlashColor = new(0.95f, 0.15f, 0.15f);

        [SerializeField, Min(0.01f)]
        [Tooltip("時間で減っていく間の、点滅の周期（秒）")]
        private float _drainingFlashPeriod = 0.5f;

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
        private ITargetHealthState _targetHealthState;
        private IFlightPromptState _flightPromptState;
        private FragmentThrowAdapter _fragmentThrowAdapter;

        private RectTransform _healthFill;
        private RectTransform _chargeFill;
        private GameObject _targetHealthRoot;
        private RectTransform _targetHealthFill;
        private Image _targetHealthFillImage;
        private Text _targetHealthNameText;
        private RectTransform _targetHealthGauge;
        /// <summary> ひびで割れたゲージの左右。0が左 </summary>
        private RectTransform[] _brokenHalves;
        private RectTransform _crackRoot;
        /// <summary> ひびが入った瞬間の揺れの残り時間（秒） </summary>
        private float _brokenShakeRemaining;
        /// <summary> 装備枠ごとの左右の線。枠の番号 × 2 ＋ 左右（0が左） </summary>
        private Image[] _skillLines;
        private GameObject[] _slots;
        private Text[] _slotCostTexts;
        private Text[] _slotStatusTexts;
        private Text _chargeValueText;
        private RectTransform _chargeDiamond;

        /// <summary> ひし形が広がり始めた時刻（Time.unscaledTime）。時間を止めても戻るように、実時間で数える </summary>
        private float _diamondPulseStart = float.NegativeInfinity;
        private Text _targetText;
        private Text _timeText;
        private Text _defeatText;
        private Text _launcherText;
        private Text _startText;
        private Text _flightText;
        private float _startHideTime = -1f;

        private int _shownHealth = REDRAW_REQUIRED;
        private int _shownHealthMax = REDRAW_REQUIRED;
        private int _shownCharge = REDRAW_REQUIRED;
        private int _shownChargeMax = REDRAW_REQUIRED;
        private int _shownDestroyedTargets = REDRAW_REQUIRED;
        private int _shownRequiredTargets = REDRAW_REQUIRED;
        private float _shownTargetHealth = REDRAW_REQUIRED;
        private float _shownTargetHealthMax = REDRAW_REQUIRED;
        private TargetHealthMode _shownTargetHealthMode = (TargetHealthMode)(-1);
        private int _shownDefeats = REDRAW_REQUIRED;
        private int _shownRequiredDefeats = REDRAW_REQUIRED;
        /// <summary> 表示した残りの秒数。制限時間がないときは -1 </summary>
        private int _shownSeconds = REDRAW_REQUIRED;
        private bool _shownTimeWarning;
        /// <summary> 表示した装填状態。0が空、1が装填済み </summary>
        private int _shownLoaded = REDRAW_REQUIRED;
        /// <summary> 枠ごとの、表示した消費量。装備していない枠は UNEQUIPPED_COST </summary>
        private int[] _shownSlotCosts;
        private bool[] _shownSlotReached;

        /// <summary>
        /// チャージのゲージの中心が camera の画面に映る方向で、camera から depth だけ前の位置を返す。吸い込む粒の行き先に使う。
        /// ゲージは HUD を組み立てたあとに位置が決まる為、組み立て前は false を返す。
        /// </summary>
        /// <param name="camera">位置を求める基準のカメラ</param>
        /// <param name="depth">カメラからの前方の距離（m）</param>
        /// <param name="point">ゲージの中心を指す位置（ワールド空間）</param>
        public bool TryGetChargeGaugePoint(Camera camera, float depth, out Vector3 point)
        {
            if (!Initialized || camera == null)
            {
                point = default;
                return false;
            }

            var viewport = new Vector3(0.5f, (CHARGE_GAUGE_MIN_Y + CHARGE_GAUGE_MAX_Y) * 0.5f, depth);
            point = camera.ViewportToWorldPoint(viewport);
            return true;
        }

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
                !blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this) ||
                !blackBoard.TryGetSceneState<StageBoard, ITargetHealthState>(out _targetHealthState, this) ||
                !blackBoard.TryGetSceneState<PlayerBoard, IFlightPromptState>(out _flightPromptState, this)) return;

            _fragmentThrowAdapter = fragmentThrowAdapter;
            BuildView();
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized) return;

            UpdateGauges();
            UpdateDiamond();
            UpdateSkills();
            UpdateTargetText();
            UpdateDefeatText();
            UpdateTargetHealth();
            UpdateTimeText();
            UpdateLauncherText();
            UpdateStartText();
            _flightText.enabled = _flightPromptState.IsVisible;
        }

        /// <summary>
        /// HP とチャージのゲージの長さと、チャージ量の数値を出す。スキルの線が読む最大量を覚えるのは UpdateSkills の後にする。
        /// </summary>
        private void UpdateGauges()
        {
            if (_healthState.Current != _shownHealth || _healthState.Max != _shownHealthMax)
            {
                _shownHealth = _healthState.Current;
                _shownHealthMax = _healthState.Max;
                SetCenteredRatio(_healthFill, (float)_shownHealth / _shownHealthMax);
            }

            if (_chargeState.Current != _shownCharge || _chargeState.Max != _shownChargeMax)
            {
                if (_shownCharge != REDRAW_REQUIRED && _chargeState.Current > _shownCharge) _diamondPulseStart = Time.unscaledTime;

                _shownCharge = _chargeState.Current;
                SetCenteredRatio(_chargeFill, (float)_shownCharge / _chargeState.Max);
                _chargeValueText.text = _shownCharge.ToString();
            }
        }

        /// <summary>
        /// チャージが増えた瞬間に大きくなったひし形を、元の大きさへ戻す。
        /// </summary>
        private void UpdateDiamond()
        {
            var progress = (Time.unscaledTime - _diamondPulseStart) / _diamondPulseDuration;
            var scale = progress >= 1f ? 1f : Mathf.Lerp(_diamondPulseScale, 1f, progress);
            _chargeDiamond.localScale = Vector3.one * scale;
        }

        /// <summary>
        /// 装備しているスキルごとに、ゲージが消費量まで溜まったときの両端の位置へ線を置き、枠に発動できるかを出す。
        /// 空きの枠と、上限を超える消費量の線は隠す。
        /// </summary>
        private void UpdateSkills()
        {
            var isMaxChanged = _chargeState.Max != _shownChargeMax;
            for (var slot = 0; slot < _slotState.SlotCount; slot++)
            {
                var isEquipped = _slotState.TryGetCost(slot, out var cost);
                var isReached = isEquipped && _chargeState.Current >= cost;
                var shownCost = isEquipped ? cost : UNEQUIPPED_COST;
                if (!isMaxChanged && shownCost == _shownSlotCosts[slot] && isReached == _shownSlotReached[slot]) continue;

                _shownSlotCosts[slot] = shownCost;
                _shownSlotReached[slot] = isReached;
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

            _shownChargeMax = _chargeState.Max;
        }

        /// <summary>
        /// 壊した目標の数と、クリアに要る数を出す。
        /// </summary>
        private void UpdateTargetText()
        {
            var destroyed = _flowState.DestroyedTargetCount;
            var required = _flowState.RequiredTargetCount;
            if (destroyed == _shownDestroyedTargets && required == _shownRequiredTargets) return;

            _shownDestroyedTargets = destroyed;
            _shownRequiredTargets = required;
            _targetText.text = $"Targets {destroyed} / {required}";
        }

        /// <summary>
        /// 倒した敵の数と、クリアに要る数を出す。クリアに敵の数を要しないステージでは隠す。
        /// </summary>
        private void UpdateDefeatText()
        {
            var defeats = _flowState.DefeatCount;
            var required = _flowState.RequiredDefeatCount;
            if (defeats == _shownDefeats && required == _shownRequiredDefeats) return;

            _shownDefeats = defeats;
            _shownRequiredDefeats = required;
            _defeatText.enabled = required > 0;
            if (_defeatText.enabled) _defeatText.text = $"撃破 {defeats} / {required}";
        }

        /// <summary>
        /// 体力制の破壊対象の名前と体力のゲージを出す。対象がないステージ（最大体力が0）では隠す。無敵の間はゲージを灰色にして名前に「無敵」を添える。
        /// 時間で減っていく間は、ゲージを灰色と赤で点滅させる。壊れて止まったら、灰色のゲージをひびの所で左右に割り、名前に「停止」を添える。
        /// </summary>
        private void UpdateTargetHealth()
        {
            var current = _targetHealthState.Current;
            var max = _targetHealthState.Max;
            var mode = _targetHealthState.Mode;
            UpdateBrokenShake();
            if (mode == TargetHealthMode.Draining && max > 0f)
            {
                var flash = Mathf.PingPong(Time.time * 2f / _drainingFlashPeriod, 1f);
                _targetHealthFillImage.color = Color.Lerp(_invincibleColor, _drainingFlashColor, flash);
            }

            if (current == _shownTargetHealth && max == _shownTargetHealthMax && mode == _shownTargetHealthMode) return;

            _shownTargetHealth = current;
            _shownTargetHealthMax = max;
            _shownTargetHealthMode = mode;
            _targetHealthRoot.SetActive(max > 0f);
            if (max <= 0f) return;

            SetCenteredRatio(_targetHealthFill, current / max);
            var isInvincible = mode == TargetHealthMode.Invincible;
            if (mode != TargetHealthMode.Draining) _targetHealthFillImage.color = isInvincible ? _invincibleColor : _targetHealthColor;
            _targetHealthNameText.text = mode switch
            {
                TargetHealthMode.Invincible => $"{_targetHealthState.Name}  無敵",
                TargetHealthMode.Broken => $"{_targetHealthState.Name}  停止",
                _ => _targetHealthState.Name,
            };
            SetBroken(mode == TargetHealthMode.Broken);
        }

        /// <summary>
        /// ゲージを、ひびで左右に割った灰色の見た目にするか、元に戻す。割るときは、ゲージを揺らし始める。
        /// </summary>
        private void SetBroken(bool isBroken)
        {
            if (isBroken && _brokenHalves == null) BuildBrokenGauge();
            if (_brokenHalves == null) return;

            var wasBroken = _crackRoot.gameObject.activeSelf;
            _targetHealthGauge.GetComponent<Image>().enabled = !isBroken;
            _targetHealthFill.gameObject.SetActive(!isBroken);
            _crackRoot.gameObject.SetActive(isBroken);
            foreach (var half in _brokenHalves)
            {
                half.gameObject.SetActive(isBroken);
            }

            if (isBroken && !wasBroken) _brokenShakeRemaining = _brokenShakeDuration;
        }

        /// <summary>
        /// ひびが入った直後の間だけ、ゲージの背景の位置をばらつかせる。終わったら元の位置に戻す。
        /// </summary>
        private void UpdateBrokenShake()
        {
            if (_brokenShakeRemaining <= 0f) return;

            _brokenShakeRemaining -= Time.unscaledDeltaTime;
            var strength = _brokenShakeRemaining > 0f ? _brokenShakeStrength * _brokenShakeRemaining / _brokenShakeDuration : 0f;
            _targetHealthGauge.anchoredPosition = Random.insideUnitCircle * strength;
        }

        /// <summary>
        /// ゲージの背景の中に、ひびの所で割れた左右の灰色の板と、ひびの線を作る。線の長さと角度は今のゲージの大きさから求めるので、最初に割るときに作る。
        /// </summary>
        private void BuildBrokenGauge()
        {
            var size = _targetHealthGauge.rect.size;
            _brokenHalves = new RectTransform[2];
            for (var i = 0; i < 2; i++)
            {
                var half = UiBuilder.CreateImage(_targetHealthGauge, i == 0 ? "BrokenLeft" : "BrokenRight", _invincibleColor).rectTransform;
                var side = i == 0 ? -1f : 1f;
                var crackX = _crackPoints[2].x;
                UiBuilder.SetAnchors(half, new Vector2(i == 0 ? 0f : crackX, 0f), new Vector2(i == 0 ? crackX : 1f, 1f));
                half.pivot = new Vector2(i == 0 ? 1f : 0f, 0.5f);
                half.anchoredPosition = new Vector2(side * _brokenGap, 0f);
                half.localRotation = Quaternion.Euler(0f, 0f, -side * _brokenTilt);
                half.gameObject.SetActive(false);
                _brokenHalves[i] = half;
            }

            _crackRoot = new GameObject("Crack", typeof(RectTransform)).GetComponent<RectTransform>();
            _crackRoot.SetParent(_targetHealthGauge, false);
            UiBuilder.SetAnchors(_crackRoot, Vector2.zero, Vector2.one);
            for (var i = 0; i + 1 < _crackPoints.Length; i++)
            {
                CreateCrackLine(_crackPoints[i], _crackPoints[i + 1], size);
            }

            for (var i = 0; i + 1 < _crackBranches.Length; i += 2)
            {
                CreateCrackLine(_crackBranches[i], _crackBranches[i + 1], size);
            }

            _crackRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// ゲージの背景に対する割合の位置 from から to へ、ひびの線を 1 本引く。
        /// </summary>
        private void CreateCrackLine(Vector2 from, Vector2 to, Vector2 gaugeSize)
        {
            var line = UiBuilder.CreateImage(_crackRoot, "CrackLine", _crackColor).rectTransform;
            var delta = Vector2.Scale(to - from, gaugeSize);
            line.anchorMin = from;
            line.anchorMax = from;
            line.pivot = new Vector2(0f, 0.5f);
            line.anchoredPosition = Vector2.zero;
            line.sizeDelta = new Vector2(delta.magnitude, _crackWidth);
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// 制限時間の残りを「分:秒」で出す。残りが警告の秒数以下なら警告の色にする。制限時間がないステージでは隠す。
        /// </summary>
        private void UpdateTimeText()
        {
            var remaining = _flowState.RemainingTime;
            var seconds = remaining >= 0f ? Mathf.CeilToInt(remaining) : -1;
            var isWarning = remaining >= 0f && remaining <= _timeWarningSeconds;
            if (seconds == _shownSeconds && isWarning == _shownTimeWarning) return;

            _shownSeconds = seconds;
            _shownTimeWarning = isWarning;
            _timeText.enabled = seconds >= 0;
            if (!_timeText.enabled) return;

            _timeText.text = $"{seconds / 60:00}:{seconds % 60:00}";
            _timeText.color = isWarning ? _timeWarningColor : _textColor;
        }

        /// <summary>
        /// ランチャーに破片が装填されているかを出す。
        /// </summary>
        private void UpdateLauncherText()
        {
            var loaded = _fragmentThrowAdapter != null && _fragmentThrowAdapter.IsLoaded ? 1 : 0;
            if (loaded == _shownLoaded) return;

            _shownLoaded = loaded;
            _launcherText.text = loaded == 1 ? "Launcher: LOADED" : "Launcher: EMPTY";
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
            _chargeFill = CreateGauge(canvas, "Charge", new Vector2(0.5f - halfWidth, CHARGE_GAUGE_MIN_Y),
                new Vector2(0.5f + halfWidth, CHARGE_GAUGE_MAX_Y), _chargeColor);

            // 数値の後ろで、ゲージの手前に置く。ゲージより大きいので、上下にはみ出して見える
            _chargeDiamond = UiBuilder.CreateImage(_chargeFill.parent, "Diamond", _diamondColor).rectTransform;
            UiBuilder.SetAnchors(_chargeDiamond, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _chargeDiamond.sizeDelta = new Vector2(DIAMOND_SIZE, DIAMOND_SIZE);
            _chargeDiamond.localRotation = Quaternion.Euler(0f, 0f, 45f);

            _chargeValueText = UiBuilder.CreateText(_chargeFill.parent, "Value", CHARGE_FONT_SIZE);
            UiBuilder.SetAnchors(_chargeValueText.rectTransform, Vector2.zero, Vector2.one);
            _chargeValueText.color = _textColor;

            // 線は、チャージのゲージの全体（減った部分の背景）に対する割合で置く
            var chargeBackground = _chargeFill.parent;
            _skillLines = new Image[_slotState.SlotCount * 2];
            _slots = new GameObject[_slotState.SlotCount];
            _slotCostTexts = new Text[_slotState.SlotCount];
            _slotStatusTexts = new Text[_slotState.SlotCount];
            _shownSlotCosts = new int[_slotState.SlotCount];
            _shownSlotReached = new bool[_slotState.SlotCount];
            System.Array.Fill(_shownSlotCosts, REDRAW_REQUIRED);
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

            _timeText = UiBuilder.CreateText(canvas, "Time", LABEL_FONT_SIZE);
            UiBuilder.SetAnchors(_timeText.rectTransform, new Vector2(0.4f, 0.86f), new Vector2(0.6f, 0.92f));
            _timeText.enabled = false;

            _defeatText = UiBuilder.CreateText(canvas, "Defeats", LABEL_FONT_SIZE);
            UiBuilder.SetAnchors(_defeatText.rectTransform, new Vector2(0.4f, 0.80f), new Vector2(0.6f, 0.86f));
            _defeatText.enabled = false;

            BuildTargetHealth(canvas);

            _launcherText = UiBuilder.CreateText(canvas, "Launcher", LABEL_FONT_SIZE, TextAnchor.MiddleRight);
            UiBuilder.SetAnchors(_launcherText.rectTransform, new Vector2(0.7f, 0.03f), new Vector2(0.98f, 0.09f));

            _startText = UiBuilder.CreateText(canvas, "Start", START_FONT_SIZE);
            UiBuilder.SetAnchors(_startText.rectTransform, new Vector2(0f, 0.55f), new Vector2(1f, 0.7f));
            _startText.text = START_TEXT;
            _startText.enabled = false;

            _flightText = UiBuilder.CreateText(canvas, "FlightPrompt", LABEL_FONT_SIZE);
            UiBuilder.SetAnchors(_flightText.rectTransform, new Vector2(0.4f, 0.3f), new Vector2(0.6f, 0.36f));
            _flightText.text = FLIGHT_PROMPT_TEXT;
            _flightText.enabled = false;
        }

        /// <summary>
        /// 体力制の破壊対象の名前と体力のゲージを、上の中央の文字の下に組み立てる。最初は隠す。
        /// </summary>
        private void BuildTargetHealth(RectTransform canvas)
        {
            _targetHealthRoot = new GameObject("TargetHealth", typeof(RectTransform));
            var rootRect = (RectTransform)_targetHealthRoot.transform;
            rootRect.SetParent(canvas, false);
            UiBuilder.SetAnchors(rootRect, new Vector2(0.3f, 0.7f), new Vector2(0.7f, 0.8f));

            _targetHealthNameText = UiBuilder.CreateText(rootRect, "Name", LABEL_FONT_SIZE);
            UiBuilder.SetAnchors(_targetHealthNameText.rectTransform, new Vector2(0f, 0.4f), Vector2.one);

            _targetHealthFill = CreateGauge(rootRect, "TargetHealthGauge", new Vector2(0.05f, 0.05f),
                new Vector2(0.95f, 0.35f), _targetHealthColor);
            _targetHealthFillImage = _targetHealthFill.GetComponent<Image>();
            _targetHealthGauge = (RectTransform)_targetHealthFill.parent;
            _targetHealthRoot.SetActive(false);
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
