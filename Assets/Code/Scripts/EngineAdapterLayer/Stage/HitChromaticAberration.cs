using UnityEngine;
using UnityEngine.Rendering;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 攻撃が当たった瞬間に画面へ色収差を掛け、短い時間で戻す Adapter。インゲームのシーンへ置く。
    /// 色収差の強さは専用の全体 Volume の weight で動かす。Volume のプロファイルに ChromaticAberration の上書きを入れ、その強さが最大のときの値になる。
    /// 呼ぶ側は静的な Play だけを知ればよく、連続で呼ばれても、そのたびに最大から戻し直すだけで強さは重ならない。時間はスローモードに左右されない実時間で進む。
    /// </summary>
    public sealed class HitChromaticAberration : MonoBehaviour
    {
        private static HitChromaticAberration _instance;

        [SerializeField]
        [Tooltip("色収差の上書きを持つ全体 Volume。weight をこの Adapter が動かす")]
        private Volume _volume;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("当たった瞬間の Volume の weight")]
        private float _peakWeight = 1f;

        [SerializeField, Min(0.01f)]
        [Tooltip("最大から 0 に戻るまでの時間（秒、実時間）")]
        private float _duration = 0.15f;

        /// <summary> 最後に Play されてからの経過時間（秒）。_activeDuration 以上なら止まっている </summary>
        private float _elapsed;

        /// <summary> 最後に Play されたときの、最大から 0 に戻るまでの時間（秒、実時間） </summary>
        private float _activeDuration;

        /// <summary>
        /// 色収差を最大から掛け直す。シーンにこの Adapter がなければ何もしない。
        /// </summary>
        /// <param name="durationScale">戻るまでの時間に掛ける倍率。大きいほど長く残り、強く感じる</param>
        public static void Play(float durationScale = 1f)
        {
            if (_instance == null) return;

            _instance._elapsed = 0f;
            _instance._activeDuration = _instance._duration * durationScale;
        }

        private void Awake()
        {
            _instance = this;
            _activeDuration = _duration;
            _elapsed = _activeDuration;

            if (_volume == null)
            {
                UsefulLogger.LogWarning("Volume が設定されていない為、命中時の色収差が出ません。", this);
                return;
            }

            _volume.weight = 0f;
        }

        private void Update()
        {
            if (_volume == null || _elapsed >= _activeDuration) return;

            _elapsed += Time.unscaledDeltaTime;
            var remaining = 1f - Mathf.Clamp01(_elapsed / _activeDuration);
            _volume.weight = _peakWeight * remaining * remaining;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
