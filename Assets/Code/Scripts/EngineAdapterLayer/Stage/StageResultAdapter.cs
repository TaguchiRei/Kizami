using System;
using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.UI;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// インゲームの流れ（IStageFlowState）がクリアか失敗になったら、リザルトの画面を出す Adapter。インゲームのシーンへ置く。
    /// クリアなら倒した敵の数とスコア、失敗なら失敗した理由と倒した敵の数を出し、同じステージをもう一度始めるボタンと、アウトゲームへ戻るボタンを置く。
    /// 画面は uGUI の Canvas を初期化のときに組み立てる。シーンに EventSystem がなければ、ボタンを押せるように作る。
    /// </summary>
    public sealed class StageResultAdapter : InitializableMonoBehaviour
    {
        private const int SORTING_ORDER = 100;
        private const int TITLE_FONT_SIZE = 64;
        private const int BODY_FONT_SIZE = 32;
        private const float BUTTON_GAP = 40f;
        private static readonly Vector2 _buttonSize = new(360f, 80f);

        [SerializeField]
        [Tooltip("リザルトの背景の色")]
        private Color _backgroundColor = new(0f, 0f, 0f, 0.6f);

        [SerializeField]
        [Tooltip("ボタンの色")]
        private Color _buttonColor = new(0.2f, 0.2f, 0.2f, 0.9f);

        private IStageFlowState _flowState;
        private Action _retry;
        private Action _backToOutGame;
        private GameObject _root;
        private Text _titleText;
        private Text _bodyText;
        private bool _isShown;

        private static string GetFailReasonText(StageFailReason reason)
        {
            return reason switch
            {
                StageFailReason.PlayerDeath => "HP reached 0",
                StageFailReason.TimeUp => "Time is up",
                _ => reason.ToString(),
            };
        }

        /// <summary>
        /// ボタンを画面の下寄りの中央から、左（side が -1）か右（side が1）へずらして置く。
        /// </summary>
        private static void PlaceButton(RectTransform buttonRect, float side)
        {
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.25f);
            buttonRect.sizeDelta = _buttonSize;
            buttonRect.anchoredPosition = new Vector2(side * (_buttonSize.x + BUTTON_GAP) * 0.5f, 0f);
        }

        /// <summary>
        /// 流れの State を取得し、非表示のリザルトの画面を組み立てる。StageFlowState の登録より後に呼ぶ。
        /// </summary>
        /// <param name="blackBoard">IStageFlowState の取得元</param>
        /// <param name="retry">もう一度始めるボタンを押したときに呼ぶ処理</param>
        /// <param name="backToOutGame">アウトゲームへ戻るボタンを押したときに呼ぶ処理</param>
        public void Initialize(IBlackBoard blackBoard, Action retry, Action backToOutGame)
        {
            if (!blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this)) return;

            _retry = retry;
            _backToOutGame = backToOutGame;
            BuildView();
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized || _isShown) return;
            if (_flowState.Phase != StagePhase.Cleared && _flowState.Phase != StagePhase.Failed) return;

            Show();
        }

        /// <summary>
        /// 結果に応じた文を入れて画面を出し、ボタンを押せるようにカーソルのロックを外す。
        /// </summary>
        private void Show()
        {
            _isShown = true;

            if (_flowState.Phase == StagePhase.Cleared)
            {
                _titleText.text = "STAGE CLEAR";
                _bodyText.text = $"Defeated: {_flowState.DefeatCount}\nScore: {_flowState.Score}";
            }
            else
            {
                _titleText.text = "FAILED";
                _bodyText.text = $"{GetFailReasonText(_flowState.FailReason)}\nDefeated: {_flowState.DefeatCount}";
            }

            _root.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// 画面全体を覆う Canvas に、背景、題、本文、もう一度始めるボタンとアウトゲームへ戻るボタンを横に並べて組み立て、非表示にしておく。
        /// </summary>
        private void BuildView()
        {
            var canvas = UiBuilder.CreateCanvas(transform, "StageResult", SORTING_ORDER, true);
            _root = canvas.gameObject;

            var background = UiBuilder.CreateImage(canvas, "Background", _backgroundColor);
            UiBuilder.SetAnchors(background.rectTransform, Vector2.zero, Vector2.one);

            _titleText = UiBuilder.CreateText(canvas, "Title", TITLE_FONT_SIZE);
            UiBuilder.SetAnchors(_titleText.rectTransform, new Vector2(0f, 0.65f), new Vector2(1f, 0.85f));

            _bodyText = UiBuilder.CreateText(canvas, "Body", BODY_FONT_SIZE);
            UiBuilder.SetAnchors(_bodyText.rectTransform, new Vector2(0f, 0.4f), new Vector2(1f, 0.65f));

            var retryButton = UiBuilder.CreateButton(canvas, "RetryButton", "Retry", BODY_FONT_SIZE, _buttonColor);
            PlaceButton((RectTransform)retryButton.transform, -1f);
            retryButton.onClick.AddListener(OnRetryClicked);

            var backButton = UiBuilder.CreateButton(canvas, "BackButton", "Back to Menu", BODY_FONT_SIZE, _buttonColor);
            PlaceButton((RectTransform)backButton.transform, 1f);
            backButton.onClick.AddListener(OnBackClicked);

            UiBuilder.EnsureEventSystem(transform);
            _root.SetActive(false);
        }

        private void OnRetryClicked()
        {
            _root.SetActive(false);
            _retry?.Invoke();
        }

        private void OnBackClicked()
        {
            _root.SetActive(false);
            _backToOutGame?.Invoke();
        }
    }
}
