using System;
using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.Serialization;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 一人称の腕の Animator を動かす Adapter。
    /// 通常攻撃の振りは、切断面の角度から 2D ブレンドの値を決めて、左右・上下の 3 つのクリップを混ぜて再生する。
    /// スキルは、スキルごとに決めた名前の Trigger を引いて、対応するクリップを再生する。
    /// 剣は、切断に属性を足す強化（Destroy・ArmorBreak）がかかっている間、Animator を Enhanced にし、強化の種類ごとのエフェクトを出す。
    /// 切り替える瞬間をスキルのアニメーションに合わせる為、かけるのは Animation Event の OnEnhance、外すのは強化が切れた通知で行う。
    /// </summary>
    public sealed class PlayerArmAdapter : MonoBehaviour
    {
        private static readonly int _blendXHash = Animator.StringToHash("BlendX");
        private static readonly int _blendYHash = Animator.StringToHash("BlendY");
        private static readonly int _swingHash = Animator.StringToHash("Swing");
        private static readonly int _enhanceHash = Animator.StringToHash("Enhance");

        [SerializeField] [Tooltip("腕の Animator。Update Mode は Unscaled Time にして、スローモード中も等速で動かす")]
        private Animator _armAnimator;

        [SerializeField] [FormerlySerializedAs("_sorwdAnimator")]
        [Tooltip("剣の Animator。Bool の Enhance で Default と Enhanced を切り替える")]
        private Animator _swordAnimator;

        [SerializeField] [Tooltip("破壊の強化がかかっている間に出す剣のエフェクト。未設定なら出さない")]
        private GameObject _destroyEffect;

        [SerializeField] [Tooltip("装甲破壊の強化がかかっている間に出す剣のエフェクト。未設定なら出さない")]
        private GameObject _armorBreakEffect;

        private IPlayerEnhanceState _enhanceState;
        private IDisposable _enhanceSubscription;

        /// <summary>
        /// PlayerEnhanceState の登録より後に呼ぶこと。
        /// </summary>
        /// <param name="blackBoard">強化ステートの取得元</param>
        public void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetSceneState<PlayerBoard, IPlayerEnhanceState>(out _enhanceState, this)) return;

            _enhanceSubscription = _enhanceState.RegisterOnEnhanceChanged(OnEnhanceChanged);
        }

        /// <summary>
        /// 切断面の角度に合わせた振りを再生する。振っている途中なら、最初から振り直す。
        /// </summary>
        /// <param name="angle">切断面の角度（度）。0 で左から右、180 で右から左、90 と 270 で上から下</param>
        public void PlaySwing(float angle)
        {
            if (_armAnimator == null) return;

            var radian = angle * Mathf.Deg2Rad;
            _armAnimator.SetFloat(_blendXHash, -Mathf.Cos(radian));
            _armAnimator.SetFloat(_blendYHash, Mathf.Sin(radian));
            _armAnimator.SetTrigger(_swingHash);
        }

        /// <summary>
        /// スキルのアニメーションを再生する。
        /// </summary>
        /// <param name="trigger">Player.controller の Trigger の名前。空なら何もしない</param>
        public void PlaySkill(string trigger)
        {
            if (_armAnimator == null || string.IsNullOrEmpty(trigger)) return;

            _armAnimator.SetTrigger(trigger);
        }

        /// <summary>
        /// 剣を Enhanced にし、かかっている強化の種類のエフェクトを出す。スキルのアニメーションの Animation Event から呼ぶ。
        /// </summary>
        public void OnEnhance()
        {
            if (_enhanceState == null) return;

            var isDestroy = _enhanceState.TryGet(EnhanceKind.Destroy, out _, out _);
            var isArmorBreak = _enhanceState.TryGet(EnhanceKind.ArmorBreak, out _, out _);
            if (!isDestroy && !isArmorBreak) return;

            if (_swordAnimator != null) _swordAnimator.SetBool(_enhanceHash, true);
            if (_destroyEffect != null && isDestroy) _destroyEffect.SetActive(true);
            if (_armorBreakEffect != null && isArmorBreak) _armorBreakEffect.SetActive(true);
        }

        /// <summary>
        /// 強化が切れたら、その種類のエフェクトを消す。属性を足す強化がすべて切れたら剣を Default に戻す。
        /// </summary>
        private void OnEnhanceChanged(EnhanceKind kind, int remainingCount)
        {
            if (remainingCount > 0) return;

            if (kind == EnhanceKind.Destroy && _destroyEffect != null) _destroyEffect.SetActive(false);
            if (kind == EnhanceKind.ArmorBreak && _armorBreakEffect != null) _armorBreakEffect.SetActive(false);

            var isEnhanced = _enhanceState.TryGet(EnhanceKind.Destroy, out _, out _)
                             || _enhanceState.TryGet(EnhanceKind.ArmorBreak, out _, out _);
            if (!isEnhanced && _swordAnimator != null) _swordAnimator.SetBool(_enhanceHash, false);
        }

        private void OnDestroy()
        {
            _enhanceSubscription?.Dispose();
        }
    }
}