using System.Collections.Generic;
using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.UI;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージの案内を画面に出す Adapter。左上に大目標・小目標・プレイヤーのやったことのログ、右上に会話を出す。インゲームのシーンへ置く。
    /// ログは、新しい行が下に入り、古い行が上へ滑らかに流れる。出しておく時間が近づくか、上限を超えて消える行は、だんだん半透明になって消える。
    /// プレイ中のときだけ出す。位置と大きさは、基準の解像度（1920x1080）での px で決める。
    /// </summary>
    public sealed class StageGuideAdapter : InitializableMonoBehaviour
    {
        private const int SORTING_ORDER = 9;
        private const int OBJECTIVE_FONT_SIZE = 30;
        private const int SUB_OBJECTIVE_FONT_SIZE = 24;
        private const int LOG_FONT_SIZE = 22;
        private const int DIALOGUE_FONT_SIZE = 26;
        private const float LEFT_MARGIN = 40f;
        private const float TOP_MARGIN = 40f;
        private const float RIGHT_MARGIN = 40f;
        private const float COLUMN_WIDTH = 560f;
        private const float OBJECTIVE_HEIGHT = 44f;
        private const float SUB_OBJECTIVE_HEIGHT = 36f;
        private const float LOG_GAP = 12f;
        private const float LOG_ROW_HEIGHT = 32f;

        /// <summary> 会話の枠の上端。右上の「倒した数」の表示と重ならない高さにする </summary>
        private const float DIALOGUE_TOP = 100f;

        private const float DIALOGUE_WIDTH = 620f;
        private const float DIALOGUE_HEIGHT = 220f;
        private const float DIALOGUE_PADDING = 16f;

        [SerializeField, Min(0f)]
        [Tooltip("行が現れるとき、不透明になるまでの時間（秒）")]
        private float _fadeInDuration = 0.2f;

        [SerializeField, Min(0.01f)]
        [Tooltip("行が消えるとき、半透明になって見えなくなるまでの時間（秒）。出しておく時間の終わりから数える")]
        private float _fadeOutDuration = 1f;

        [SerializeField, Min(0.1f)]
        [Tooltip("行が上へ流れる速さ。大きいほど速く目的の位置に着く")]
        private float _scrollSharpness = 14f;

        [SerializeField]
        [Tooltip("会話の枠の色")]
        private Color _dialogueBackgroundColor = new(0f, 0f, 0f, 0.5f);

        private readonly List<LogRow> _rows = new();
        private readonly Dictionary<int, LogRow> _rowsById = new();

        private IStageGuideState _guideState;
        private IStageFlowState _flowState;

        private GameObject _canvasObject;
        private RectTransform _logContainer;
        private Text _objectiveText;
        private Text _subObjectiveText;
        private GameObject _dialogueBox;
        private Text _dialogueText;

        /// <summary>
        /// 親の隅を基準に、その隅と同じ側を軸にして、位置と大きさを決める。
        /// </summary>
        private static void Place(RectTransform rect, Vector2 corner, Vector2 position, Vector2 size)
        {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>
        /// 必要な State を取得し、画面を組み立てる。案内と流れの State の登録より後に呼ぶ。
        /// </summary>
        /// <param name="blackBoard">案内と流れの State の取得元</param>
        public void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetSceneState<StageBoard, IStageGuideState>(out _guideState, this) ||
                !blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this)) return;

            BuildView();
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized) return;

            var isVisible = _flowState.Phase == StagePhase.Playing;
            if (_canvasObject.activeSelf != isVisible) _canvasObject.SetActive(isVisible);
            if (!isVisible) return;

            _objectiveText.text = _guideState.Objective;
            _subObjectiveText.text = _guideState.SubObjective;

            var hasDialogue = !string.IsNullOrEmpty(_guideState.Dialogue);
            if (_dialogueBox.activeSelf != hasDialogue) _dialogueBox.SetActive(hasDialogue);
            if (hasDialogue) _dialogueText.text = _guideState.Dialogue;

            UpdateLogs(Time.unscaledTime, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// State のログに合わせて行を足し、消し、位置と透明度を進める。
        /// 行の位置は、新しい行を一番下の枠として、古い行ほど上の枠にする。枠の一番上に、上限を超えて押し出された行が消えるための1行分を空けてある。
        /// </summary>
        private void UpdateLogs(float now, float deltaTime)
        {
            SyncRows(now);

            var smoothing = 1f - Mathf.Exp(-_scrollSharpness * deltaTime);
            var maxCount = _guideState.LogMaxCount;
            var rank = 0;
            for (var i = _rows.Count - 1; i >= 0; i--, rank++)
            {
                var row = _rows[i];
                var targetY = -(maxCount - rank) * LOG_ROW_HEIGHT;
                row.Y = Mathf.Lerp(row.Y, targetY, smoothing);
                row.Rect.anchoredPosition = new Vector2(0f, row.Y);

                var alpha = GetAlpha(row, now);
                var color = row.Text.color;
                color.a = alpha;
                row.Text.color = color;
                row.Shadow.effectColor = new Color(0f, 0f, 0f, 0.8f * alpha);
            }

            for (var i = _rows.Count - 1; i >= 0; i--)
            {
                if (GetFadeOutAlpha(_rows[i], now) > 0f) continue;

                _rowsById.Remove(_rows[i].Id);
                Destroy(_rows[i].Rect.gameObject);
                _rows.RemoveAt(i);
            }
        }

        /// <summary>
        /// State にあるログの行を作り・書き換え、State から消えた行に消え始めた時刻を付ける。出しておく時間を過ぎたログの行は作らない。
        /// </summary>
        private void SyncRows(float now)
        {
            foreach (var row in _rows)
            {
                row.IsPresent = false;
            }

            foreach (var entry in _guideState.Logs)
            {
                if (!_rowsById.TryGetValue(entry.Id, out var row))
                {
                    if (now - entry.Time >= _guideState.LogLifetime) continue;

                    row = CreateRow(entry.Id, now);
                }

                row.IsPresent = true;
                row.Time = entry.Time;
                row.Text.text = entry.Count > 1 ? $"{entry.Text} ×{entry.Count}" : entry.Text;
            }

            foreach (var row in _rows)
            {
                if (!row.IsPresent && row.RemovedTime < 0f) row.RemovedTime = now;
            }
        }

        /// <summary>
        /// 行の透明度を、現れるときと消えるときのうち、透けている方にする。
        /// </summary>
        private float GetAlpha(LogRow row, float now)
        {
            var fadeIn = _fadeInDuration > 0f ? Mathf.Clamp01((now - row.CreatedTime) / _fadeInDuration) : 1f;
            return Mathf.Min(fadeIn, GetFadeOutAlpha(row, now));
        }

        /// <summary>
        /// 消えるときの透明度を、出しておく時間の終わりと、State から消えたときのうち、透けている方にする。0になった行は消してよい。
        /// </summary>
        private float GetFadeOutAlpha(LogRow row, float now)
        {
            var remaining = _guideState.LogLifetime - (now - row.Time);
            var expired = Mathf.Clamp01(remaining / _fadeOutDuration);
            var removed = row.RemovedTime >= 0f ? Mathf.Clamp01(1f - (now - row.RemovedTime) / _fadeOutDuration) : 1f;
            return Mathf.Min(expired, removed);
        }

        /// <summary>
        /// 行を作る。目的の位置より1行分下から始めて、上へ滑り込ませる。
        /// </summary>
        private LogRow CreateRow(int id, float now)
        {
            var text = UiBuilder.CreateText(_logContainer, $"Log{id}", LOG_FONT_SIZE, TextAnchor.MiddleLeft);
            Place(text.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(COLUMN_WIDTH, LOG_ROW_HEIGHT));

            var row = new LogRow
            {
                Id = id,
                Text = text,
                Rect = text.rectTransform,
                Shadow = text.gameObject.AddComponent<Shadow>(),
                CreatedTime = now,
                RemovedTime = -1f,
                Y = -(_guideState.LogMaxCount + 1) * LOG_ROW_HEIGHT
            };
            _rows.Add(row);
            _rowsById.Add(id, row);
            return row;
        }

        /// <summary>
        /// 画面に重ねる Canvas に、左上の目標とログ、右上の会話を組み立てる。ログの枠は、上限の件数に退場用の1行を足した高さで、枠の外へ出た行を隠す。
        /// </summary>
        private void BuildView()
        {
            var canvas = UiBuilder.CreateCanvas(transform, "StageGuide", SORTING_ORDER, false);
            _canvasObject = canvas.gameObject;
            var topLeft = new Vector2(0f, 1f);

            _objectiveText = UiBuilder.CreateText(canvas, "Objective", OBJECTIVE_FONT_SIZE, TextAnchor.MiddleLeft);
            _objectiveText.fontStyle = FontStyle.Bold;
            Place(_objectiveText.rectTransform, topLeft, new Vector2(LEFT_MARGIN, -TOP_MARGIN),
                new Vector2(COLUMN_WIDTH, OBJECTIVE_HEIGHT));

            _subObjectiveText = UiBuilder.CreateText(canvas, "SubObjective", SUB_OBJECTIVE_FONT_SIZE,
                TextAnchor.MiddleLeft);
            Place(_subObjectiveText.rectTransform, topLeft, new Vector2(LEFT_MARGIN, -(TOP_MARGIN + OBJECTIVE_HEIGHT)),
                new Vector2(COLUMN_WIDTH, SUB_OBJECTIVE_HEIGHT));

            var logTop = TOP_MARGIN + OBJECTIVE_HEIGHT + SUB_OBJECTIVE_HEIGHT + LOG_GAP;
            var logs = UiBuilder.CreateImage(canvas, "Logs", Color.clear);
            logs.gameObject.AddComponent<RectMask2D>();
            _logContainer = logs.rectTransform;
            Place(_logContainer, topLeft, new Vector2(LEFT_MARGIN, -logTop),
                new Vector2(COLUMN_WIDTH, (_guideState.LogMaxCount + 1) * LOG_ROW_HEIGHT));

            var dialogue = UiBuilder.CreateImage(canvas, "Dialogue", _dialogueBackgroundColor);
            _dialogueBox = dialogue.gameObject;
            var topRight = new Vector2(1f, 1f);
            Place(dialogue.rectTransform, topRight, new Vector2(-RIGHT_MARGIN, -DIALOGUE_TOP),
                new Vector2(DIALOGUE_WIDTH, DIALOGUE_HEIGHT));

            _dialogueText = UiBuilder.CreateText(dialogue.transform, "Text", DIALOGUE_FONT_SIZE, TextAnchor.UpperLeft);
            _dialogueText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _dialogueText.verticalOverflow = VerticalWrapMode.Truncate;
            UiBuilder.SetAnchors(_dialogueText.rectTransform, Vector2.zero, Vector2.one);
            _dialogueText.rectTransform.offsetMin = new Vector2(DIALOGUE_PADDING, DIALOGUE_PADDING);
            _dialogueText.rectTransform.offsetMax = new Vector2(-DIALOGUE_PADDING, -DIALOGUE_PADDING);
            _dialogueBox.SetActive(false);
        }

        /// <summary>
        /// ログの1行の表示。
        /// </summary>
        private sealed class LogRow
        {
            /// <summary> State のログの番号 </summary>
            public int Id;

            public Text Text;
            public RectTransform Rect;
            public Shadow Shadow;

            /// <summary> 行を作った時刻（実時間の秒） </summary>
            public float CreatedTime;

            /// <summary> State のログを最後に書き換えた時刻（実時間の秒） </summary>
            public float Time;

            /// <summary> State から消えた時刻（実時間の秒）。まだ State にあれば負の値 </summary>
            public float RemovedTime;

            /// <summary> 今の縦の位置。行の上端の、枠の上端からの距離を負の値で持つ </summary>
            public float Y;

            /// <summary> State にまだあるか </summary>
            public bool IsPresent;
        }
    }
}
