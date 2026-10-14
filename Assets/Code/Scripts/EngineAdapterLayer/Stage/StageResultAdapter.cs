using System;
using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// インゲームの流れ（IStageFlowState）がクリアか失敗になったら、リザルトの画面を出す Adapter。インゲームのシーンへ置く。
    /// クリアなら倒した敵の数とスコア、失敗なら失敗した理由と倒した敵の数を出し、アウトゲームへ戻るボタンを置く。
    /// 画面は uGUI の Canvas を初期化のときに組み立てる。シーンに EventSystem がなければ、ボタンを押せるように作る。
    /// </summary>
    // TODO: 見た目を作り込むときは、組み立てをプレハブに置き換える
    public sealed class StageResultAdapter : InitializableMonoBehaviour
    {
        private const int SORTING_ORDER = 100;
        private const int TITLE_FONT_SIZE = 64;
        private const int BODY_FONT_SIZE = 32;
        private static readonly Vector2 _referenceResolution = new(1920f, 1080f);
        private static readonly Vector2 _buttonSize = new(360f, 80f);

        [SerializeField]
        [Tooltip("リザルトの背景の色")]
        private Color _backgroundColor = new(0f, 0f, 0f, 0.6f);

        [SerializeField]
        [Tooltip("ボタンの色")]
        private Color _buttonColor = new(0.2f, 0.2f, 0.2f, 0.9f);

        private IStageFlowState _flowState;
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
                _ => reason.ToString(),
            };
        }

        private static T CreateChild<T>(Transform parent, string name) where T : Component
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(T));
            child.transform.SetParent(parent, false);
            return child.GetComponent<T>();
        }

        private static Text CreateText(Transform parent, string name, Font font, int fontSize)
        {
            var text = CreateChild<Text>(parent, name);
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return text;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 流れの State を取得し、非表示のリザルトの画面を組み立てる。StageFlowState の登録より後に呼ぶ。
        /// </summary>
        /// <param name="blackBoard">IStageFlowState の取得元</param>
        /// <param name="backToOutGame">アウトゲームへ戻るボタンを押したときに呼ぶ処理</param>
        public void Initialize(IBlackBoard blackBoard, Action backToOutGame)
        {
            if (!blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this)) return;

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
        /// 画面全体を覆う Canvas に、背景、題、本文、アウトゲームへ戻るボタンを組み立てて、非表示にしておく。
        /// </summary>
        private void BuildView()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _root = new GameObject("StageResult", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _root.transform.SetParent(transform, false);

            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;

            var background = CreateChild<Image>(_root.transform, "Background");
            Stretch(background.rectTransform, Vector2.zero, Vector2.one);
            background.color = _backgroundColor;

            _titleText = CreateText(_root.transform, "Title", font, TITLE_FONT_SIZE);
            Stretch(_titleText.rectTransform, new Vector2(0f, 0.65f), new Vector2(1f, 0.85f));

            _bodyText = CreateText(_root.transform, "Body", font, BODY_FONT_SIZE);
            Stretch(_bodyText.rectTransform, new Vector2(0f, 0.4f), new Vector2(1f, 0.65f));

            var buttonImage = CreateChild<Image>(_root.transform, "BackButton");
            buttonImage.color = _buttonColor;
            var buttonRect = buttonImage.rectTransform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.25f);
            buttonRect.sizeDelta = _buttonSize;
            var button = buttonImage.gameObject.AddComponent<Button>();
            button.onClick.AddListener(OnBackClicked);

            var buttonText = CreateText(buttonRect, "Label", font, BODY_FONT_SIZE);
            Stretch(buttonText.rectTransform, Vector2.zero, Vector2.one);
            buttonText.text = "Back to Menu";

            EnsureEventSystem();
            _root.SetActive(false);
        }

        private void OnBackClicked()
        {
            _root.SetActive(false);
            _backToOutGame?.Invoke();
        }

        /// <summary>
        /// シーンに EventSystem がなければ、新しい Input System 用の入力モジュールを付けて作る。
        /// </summary>
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(transform, false);
        }
    }
}
