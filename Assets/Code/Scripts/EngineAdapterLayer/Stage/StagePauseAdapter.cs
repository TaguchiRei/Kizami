using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// インゲームの流れ（IStageFlowState）がポーズ中の間、ポーズの画面を出す Adapter。インゲームのシーンへ置く。
    /// 画面には、プレイに戻るボタンとアウトゲームへ戻るボタンを置く。出している間はカーソルのロックを外し、閉じたらロックし直す。
    /// 画面は uGUI の Canvas を初期化のときに組み立てる。
    /// </summary>
    public sealed class StagePauseAdapter : InitializableMonoBehaviour
    {
        private const int SORTING_ORDER = 90;
        private const int TITLE_FONT_SIZE = 64;
        private const int BUTTON_FONT_SIZE = 32;
        private const string TITLE_TEXT = "PAUSED";
        private static readonly Vector2 _buttonSize = new(360f, 80f);

        [SerializeField]
        [Tooltip("ポーズの画面の背景の色")]
        private Color _backgroundColor = new(0f, 0f, 0f, 0.4f);

        [SerializeField]
        [Tooltip("ボタンの色")]
        private Color _buttonColor = new(0.2f, 0.2f, 0.2f, 0.9f);

        private IStageFlowState _flowState;
        private Action _resume;
        private Action _backToOutGame;
        private GameObject _root;

        /// <summary>
        /// 流れの State を取得し、非表示のポーズの画面を組み立てる。StageFlowState の登録より後に呼ぶ。
        /// </summary>
        /// <param name="blackBoard">IStageFlowState の取得元</param>
        /// <param name="resume">プレイに戻るボタンを押したときに呼ぶ処理</param>
        /// <param name="backToOutGame">アウトゲームへ戻るボタンを押したときに呼ぶ処理</param>
        public void Initialize(IBlackBoard blackBoard, Action resume, Action backToOutGame)
        {
            if (!blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this)) return;

            _resume = resume;
            _backToOutGame = backToOutGame;
            BuildView();
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized || _flowState.IsPaused == _root.activeSelf) return;

            _root.SetActive(_flowState.IsPaused);
            Cursor.lockState = _flowState.IsPaused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _flowState.IsPaused;
        }

        /// <summary>
        /// 画面全体を覆う Canvas に、背景、題、プレイに戻るボタン、アウトゲームへ戻るボタンを組み立てて、非表示にしておく。
        /// </summary>
        private void BuildView()
        {
            var canvas = UiBuilder.CreateCanvas(transform, "StagePause", SORTING_ORDER, true);
            _root = canvas.gameObject;

            var background = UiBuilder.CreateImage(canvas, "Background", _backgroundColor);
            UiBuilder.SetAnchors(background.rectTransform, Vector2.zero, Vector2.one);

            var title = UiBuilder.CreateText(canvas, "Title", TITLE_FONT_SIZE);
            UiBuilder.SetAnchors(title.rectTransform, new Vector2(0f, 0.6f), new Vector2(1f, 0.8f));
            title.text = TITLE_TEXT;

            CreateButton(canvas, "ResumeButton", "Resume", 0.45f, () => _resume?.Invoke());
            CreateButton(canvas, "BackButton", "Back to Menu", 0.32f, () => _backToOutGame?.Invoke());

            UiBuilder.EnsureEventSystem(transform);
            _root.SetActive(false);
        }

        private void CreateButton(RectTransform canvas, string name, string label, float y, Action onClick)
        {
            var button = UiBuilder.CreateButton(canvas, name, label, BUTTON_FONT_SIZE, _buttonColor);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, y);
            rect.sizeDelta = _buttonSize;
            button.onClick.AddListener(() => onClick());
        }
    }
}
