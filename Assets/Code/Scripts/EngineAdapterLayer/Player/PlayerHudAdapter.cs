using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.Serialization;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 画面の下端に、HP のゲージと、その上にチャージのゲージを OnGUI で描く仮の HUD。インゲームのシーンへ置く。
    /// ゲージは量が減ると両端から中央へ縮む。チャージのゲージには、装備しているスキルの消費量の位置に、発動できるようになる線を左右対称に引く。
    /// </summary>
    // TODO: 本番の HUD に置き換える
    public sealed class PlayerHudAdapter : InitializableMonoBehaviour
    {
        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("ゲージの横幅（画面の横幅に対する割合）")]
        private float _widthRatio = 0.5f;

        [SerializeField, Min(1f)]
        [Tooltip("HP のゲージの高さ（px）")]
        private float _healthHeight = 14f;

        [SerializeField, Min(1f)]
        [Tooltip("チャージのゲージの高さ（px）")]
        private float _chargeHeight = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("画面の下端から HP のゲージまでの余白（px）")]
        private float _bottomMargin = 24f;

        [SerializeField, Min(0f)]
        [Tooltip("HP のゲージとチャージのゲージの間隔（px）")]
        private float _gap = 6f;

        [SerializeField, Min(1f)]
        [Tooltip("発動できるようになる線の太さ（px）")]
        private float _lineWidth = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("発動できるようになる線を、ゲージの上下へはみ出させる長さ（px）")]
        private float _lineOverhang = 3f;

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
        [Tooltip("チャージが消費量に届いているスキルの線の色")]
        private Color _reachedLineColor = Color.white;

        [SerializeField]
        [Tooltip("チャージが消費量に届いていないスキルの線の色")]
        [FormerlySerializedAs("_unreachedLineColor")]
        private Color _chargingLineColor = new(1f, 1f, 1f, 0.35f);

        private IPlayerHealthState _healthState;
        private IChargeState _chargeState;
        private ISkillSlotState _slotState;

        /// <summary>
        /// 中心の x 座標から左右へ、割合の分だけ伸ばした矩形を返す。
        /// </summary>
        private static Rect CenteredRect(float centerX, float y, float fullWidth, float ratio, float height)
        {
            var width = fullWidth * Mathf.Clamp01(ratio);
            return new Rect(centerX - width * 0.5f, y, width, height);
        }

        /// <summary>
        /// PlayerInitializer から呼ばれる。HP・チャージ・装備枠の State の登録より後に呼ぶこと。
        /// </summary>
        /// <param name="blackBoard">HP・チャージ・装備枠の取得元</param>
        public void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetSceneState<PlayerBoard, IPlayerHealthState>(out _healthState, this) ||
                !blackBoard.TryGetSceneState<PlayerBoard, IChargeState>(out _chargeState, this) ||
                !blackBoard.TryGetSceneState<PlayerBoard, ISkillSlotState>(out _slotState, this)) return;

            Initialize();
        }

        private void OnGUI()
        {
            var fullWidth = Screen.width * _widthRatio;
            var centerX = Screen.width * 0.5f;
            var healthY = Screen.height - _bottomMargin - _healthHeight;
            var chargeY = healthY - _gap - _chargeHeight;

            DrawGauge(centerX, healthY, fullWidth, _healthHeight, (float)_healthState.Current / _healthState.Max,
                _healthColor);
            DrawGauge(centerX, chargeY, fullWidth, _chargeHeight, (float)_chargeState.Current / _chargeState.Max,
                _chargeColor);
            DrawSkillLines(centerX, chargeY, fullWidth);

            GUI.color = Color.white;
        }

        private void DrawGauge(float centerX, float y, float fullWidth, float height, float ratio, Color color)
        {
            GUI.color = _backgroundColor;
            GUI.DrawTexture(CenteredRect(centerX, y, fullWidth, 1f, height), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(CenteredRect(centerX, y, fullWidth, ratio, height), Texture2D.whiteTexture);
        }

        /// <summary>
        /// 装備しているスキルごとに、ゲージが消費量まで溜まったときの両端の位置へ線を引く。上限を超える消費量の線は引かない。
        /// </summary>
        private void DrawSkillLines(float centerX, float chargeY, float fullWidth)
        {
            var lineY = chargeY - _lineOverhang;
            var lineHeight = _chargeHeight + _lineOverhang * 2f;

            for (var slot = 0; slot < _slotState.SlotCount; slot++)
            {
                if (!_slotState.TryGetCost(slot, out var cost) || cost > _chargeState.Max) continue;

                var halfWidth = fullWidth * 0.5f * cost / _chargeState.Max;
                GUI.color = _chargeState.Current >= cost ? _reachedLineColor : _chargingLineColor;
                GUI.DrawTexture(new Rect(centerX - halfWidth - _lineWidth * 0.5f, lineY, _lineWidth, lineHeight),
                    Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(centerX + halfWidth - _lineWidth * 0.5f, lineY, _lineWidth, lineHeight),
                    Texture2D.whiteTexture);
            }
        }
    }
}
