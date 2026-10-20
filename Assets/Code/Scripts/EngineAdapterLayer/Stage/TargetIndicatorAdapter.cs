using UnityEngine;
using UnityEngine.UI;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージの目標のうちプレイヤーに最も近いものの位置に、ひし形の印と距離（m）を出す Adapter。目標は TargetSelectorBase から毎フレーム選び直す。
    /// 目標が画面の外かカメラの後ろにあるときは、目標の向きの画面の端に印を寄せる。目標がないときは印を隠す。
    /// 画面は uGUI の Canvas を初期化のときに組み立てる。
    /// </summary>
    public sealed class TargetIndicatorAdapter : InitializableMonoBehaviour
    {
        private const int SORTING_ORDER = 9;
        private const int DISTANCE_FONT_SIZE = 22;
        private const float MARKER_SIZE = 24f;

        /// <summary> 画面の端に寄せたときの、端からの余白（px） </summary>
        private const float EDGE_MARGIN = 48f;

        /// <summary> 印から距離の文字までの縦の間隔（px） </summary>
        private const float TEXT_OFFSET = 28f;

        private static readonly Color _markerColor = new(1f, 0.85f, 0.2f);

        private TargetSelectorBase _selector;
        private Transform _player;
        private Camera _camera;
        private RectTransform _root;
        private Text _distanceText;

        /// <summary> 表示した距離（m）。次のフレームで必ず書き直すときは -1 </summary>
        private int _shownDistance = -1;

        /// <summary>
        /// 印を組み立て、目標を選ぶ物と距離の基準を設定する。
        /// </summary>
        /// <param name="selector">目標を選ぶ物</param>
        /// <param name="player">距離の基準にするプレイヤーの Transform</param>
        public void Initialize(TargetSelectorBase selector, Transform player)
        {
            _selector = selector;
            _player = player;
            BuildView();
            base.Initialize();
        }

        private void LateUpdate()
        {
            if (!Initialized) return;

            if (_camera == null) _camera = Camera.main;
            if (_selector == null || _player == null || _camera == null ||
                !_selector.TryGetNearest(_player.position, out var target))
            {
                _root.gameObject.SetActive(false);
                return;
            }

            _root.gameObject.SetActive(true);
            _root.position = ToScreenPosition(target);

            var distance = Mathf.RoundToInt(Vector3.Distance(_player.position, target));
            if (distance == _shownDistance) return;

            _shownDistance = distance;
            _distanceText.text = $"{distance}m";
        }

        /// <summary>
        /// 目標の画面上の位置を返す。画面の外かカメラの後ろなら、画面の中心から目標の向きへ伸ばした線が、余白を取った画面の端と交わる位置を返す。
        /// </summary>
        private Vector3 ToScreenPosition(Vector3 target)
        {
            var screen = _camera.WorldToScreenPoint(target);
            var isBehind = screen.z < 0f;
            var min = new Vector2(EDGE_MARGIN, EDGE_MARGIN);
            var max = new Vector2(Screen.width - EDGE_MARGIN, Screen.height - EDGE_MARGIN);
            if (!isBehind && screen.x >= min.x && screen.x <= max.x && screen.y >= min.y && screen.y <= max.y)
            {
                return new Vector3(screen.x, screen.y, 0f);
            }

            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            // カメラの後ろの点は画面上で反転して映る為、向きを戻す
            var direction = ((Vector2)screen - center) * (isBehind ? -1f : 1f);
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.down;

            var half = max - center;
            var scale = Mathf.Min(Mathf.Abs(half.x / direction.x), Mathf.Abs(half.y / direction.y));
            var edge = center + direction * scale;
            return new Vector3(edge.x, edge.y, 0f);
        }

        private void BuildView()
        {
            var canvas = UiBuilder.CreateCanvas(transform, "TargetIndicator", SORTING_ORDER, false);

            _root = new GameObject("Indicator", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(canvas, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.zero;
            _root.sizeDelta = Vector2.zero;

            var marker = UiBuilder.CreateImage(_root, "Marker", _markerColor).rectTransform;
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(MARKER_SIZE, MARKER_SIZE);
            marker.localRotation = Quaternion.Euler(0f, 0f, 45f);

            _distanceText = UiBuilder.CreateText(_root, "Distance", DISTANCE_FONT_SIZE);
            var textRect = _distanceText.rectTransform;
            textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(160f, 32f);
            textRect.anchoredPosition = new Vector2(0f, -TEXT_OFFSET);
            _distanceText.color = _markerColor;
            _root.gameObject.SetActive(false);
        }
    }
}
