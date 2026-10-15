using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 仮の HUD とリザルトの uGUI を、コードで組み立てるための関数群。
    /// </summary>
    // TODO: 見た目を作り込むときは、組み立てをプレハブに置き換え、このクラスを消す
    public static class UiBuilder
    {
        private static readonly Vector2 _referenceResolution = new(1920f, 1080f);

        private static Font _font;

        /// <summary> 文字に使うフォント（組み込みの LegacyRuntime） </summary>
        public static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>
        /// 画面に重ねる Canvas を、親の子として作る。基準の解像度は1920x1080で、画面の大きさに合わせて拡大縮小する。
        /// </summary>
        /// <param name="parent">親</param>
        /// <param name="name">GameObject の名前</param>
        /// <param name="sortingOrder">描く順。大きいほど手前</param>
        /// <param name="isInteractive">ボタンを押せるようにするか</param>
        public static RectTransform CreateCanvas(Transform parent, string name, int sortingOrder, bool isInteractive)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(parent, false);
            if (isInteractive) root.AddComponent<GraphicRaycaster>();

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;

            return (RectTransform)root.transform;
        }

        /// <summary>
        /// 単色の Image を作る。
        /// </summary>
        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var image = CreateChild<Image>(parent, name);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// 中央揃えの白い Text を作る。
        /// </summary>
        public static Text CreateText(Transform parent, string name, int fontSize, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var text = CreateChild<Text>(parent, name);
            text.font = Font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>
        /// 文字の付いたボタンを作る。
        /// </summary>
        public static Button CreateButton(Transform parent, string name, string label, int fontSize, Color color)
        {
            var image = CreateChild<Image>(parent, name);
            image.color = color;
            var button = image.gameObject.AddComponent<Button>();

            var text = CreateText(image.transform, "Label", fontSize);
            text.text = label;
            SetAnchors(text.rectTransform, Vector2.zero, Vector2.one);
            return button;
        }

        /// <summary>
        /// 親に対する割合で範囲を決め、余白を0にする。
        /// </summary>
        public static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// シーンに EventSystem がなければ、新しい Input System 用の入力モジュールを付けて、親の子として作る。
        /// </summary>
        public static void EnsureEventSystem(Transform parent)
        {
            if (EventSystem.current != null || Object.FindAnyObjectByType<EventSystem>() != null) return;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(parent, false);
        }

        private static T CreateChild<T>(Transform parent, string name) where T : Component
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(T));
            child.transform.SetParent(parent, false);
            return child.GetComponent<T>();
        }
    }
}
