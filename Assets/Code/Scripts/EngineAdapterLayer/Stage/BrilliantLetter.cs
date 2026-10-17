using UnityEngine;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// Brilliant Skill の文字 1 文字。出現の演出と、手前に描く期間を担う。当たり判定は持たず、言葉全体の当たり判定（BrilliantWord）に任せる。
    /// 出るときは、遅らせた時刻から、大きくなりながら回り、縮んで元の大きさと向きに落ち着く。出てから少しの間は、敵や建物より手前に描く（深度テストを Always にした材質）。
    /// </summary>
    public sealed class BrilliantLetter : MonoBehaviour
    {
        /// <summary> 最も大きくなる時点の、出現にかける時間に対する割合 </summary>
        private const float POP_PEAK_RATIO = 0.5f;

        private float _popStartTime;
        private float _popDuration;
        private float _popPeakScale;
        private float _popSpinDegrees;
        private float _frontDuration;
        private bool _isPopping;
        private bool _isInFront;
        private Quaternion _baseLocalRotation;
        private Renderer _fillRenderer;
        private Renderer _outlineRenderer;
        private Material _fillMaterial;
        private Material _outlineMaterial;

        /// <summary> この文字を切る対象 </summary>
        public CuttableObject Cuttable { get; private set; }

        /// <summary> この文字が属する言葉 </summary>
        public BrilliantWord Word { get; set; }

        /// <summary>
        /// 出現の演出を始める。遅らせた時刻まで大きさ 0 で待ち、そこから大きさ 0 → peakScale → 1 と変わり、向きは spinDegrees 回った所から元の向きまで回る。
        /// 時間はゲーム内の時間の倍率に関係なく進む。
        /// </summary>
        /// <param name="delay">演出を始めるまでの時間（秒）</param>
        /// <param name="duration">演出にかける時間（秒）</param>
        /// <param name="peakScale">最も大きくなったときの大きさ</param>
        /// <param name="spinDegrees">演出の始めに、元の向きから文字の面の中で回っている角度（度）</param>
        public void StartPop(float delay, float duration, float peakScale, float spinDegrees)
        {
            _popStartTime = Time.unscaledTime + delay;
            _popDuration = duration;
            _popPeakScale = peakScale;
            _popSpinDegrees = spinDegrees;
            _baseLocalRotation = transform.localRotation;
            _isPopping = true;
            transform.localScale = Vector3.zero;
        }

        /// <summary>
        /// 出てから少しの間、敵や建物より手前に描く。時間が過ぎたら、普通に描く材質に戻す。
        /// </summary>
        /// <param name="fillRenderer">文字の Renderer</param>
        /// <param name="outlineRenderer">輪郭線の殻の Renderer</param>
        /// <param name="fillOverlay">手前に描く文字の材質</param>
        /// <param name="outlineOverlay">手前に描く輪郭線の材質</param>
        /// <param name="fillNormal">普通に描く文字の材質</param>
        /// <param name="outlineNormal">普通に描く輪郭線の材質</param>
        /// <param name="frontDuration">手前に描く時間（秒）。出現の演出の始まりから数える</param>
        public void SetFrontPhase(Renderer fillRenderer, Renderer outlineRenderer, Material fillOverlay,
            Material outlineOverlay, Material fillNormal, Material outlineNormal, float frontDuration)
        {
            _fillRenderer = fillRenderer;
            _outlineRenderer = outlineRenderer;
            _fillMaterial = fillNormal;
            _outlineMaterial = outlineNormal;
            _frontDuration = frontDuration;
            _isInFront = true;
            _fillRenderer.sharedMaterial = fillOverlay;
            _outlineRenderer.sharedMaterial = outlineOverlay;
        }

        private void Awake()
        {
            Cuttable = GetComponent<CuttableObject>();
        }

        private void Update()
        {
            if (_isInFront && Time.unscaledTime - _popStartTime >= _frontDuration) EndFrontPhase();
            if (!_isPopping) return;

            var elapsed = Time.unscaledTime - _popStartTime;
            if (elapsed < 0f) return;

            var progress = Mathf.Clamp01(elapsed / _popDuration);
            transform.localScale = Vector3.one * EvaluatePopScale(progress, _popPeakScale);
            var spin = _popSpinDegrees * (1f - EaseOut(progress));
            transform.localRotation = _baseLocalRotation * Quaternion.AngleAxis(spin, Vector3.forward);
            if (progress < 1f) return;

            transform.localRotation = _baseLocalRotation;
            _isPopping = false;
        }

        /// <summary>
        /// 手前に描く材質から、普通に描く材質に戻す。
        /// </summary>
        private void EndFrontPhase()
        {
            if (!_isInFront) return;

            _isInFront = false;
            _fillRenderer.sharedMaterial = _fillMaterial;
            _outlineRenderer.sharedMaterial = _outlineMaterial;
        }

        /// <summary>
        /// 出現の進み具合（0〜1）に対する文字の大きさ。0 から peakScale まで勢いよく大きくなり、1 まで縮む。
        /// </summary>
        private static float EvaluatePopScale(float progress, float peakScale)
        {
            if (progress < POP_PEAK_RATIO)
            {
                return peakScale * EaseOut(progress / POP_PEAK_RATIO);
            }

            var settle = (progress - POP_PEAK_RATIO) / (1f - POP_PEAK_RATIO);
            return Mathf.Lerp(peakScale, 1f, settle * settle * (3f - 2f * settle));
        }

        private static float EaseOut(float progress)
        {
            var remaining = 1f - progress;
            return 1f - remaining * remaining * remaining;
        }
    }
}
