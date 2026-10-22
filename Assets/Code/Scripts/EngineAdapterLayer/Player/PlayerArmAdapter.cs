using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 一人称の腕の Animator を動かす Adapter。
    /// 通常攻撃の振りは、切断面の角度から 2D ブレンドの値を決めて、左右・上下の 3 つのクリップを混ぜて再生する。
    /// </summary>
    public sealed class PlayerArmAdapter : MonoBehaviour
    {
        private static readonly int _blendXHash = Animator.StringToHash("BlendX");
        private static readonly int _blendYHash = Animator.StringToHash("BlendY");
        private static readonly int _swingHash = Animator.StringToHash("Swing");

        [SerializeField]
        [Tooltip("腕の Animator。Update Mode は Unscaled Time にして、スローモード中も等速で動かす")]
        private Animator _animator;

        /// <summary>
        /// 切断面の角度に合わせた振りを再生する。振っている途中なら、最初から振り直す。
        /// </summary>
        /// <param name="angle">切断面の角度（度）。0 で左から右、180 で右から左、90 と 270 で上から下</param>
        public void PlaySwing(float angle)
        {
            if (_animator == null) return;

            var radian = angle * Mathf.Deg2Rad;
            _animator.SetFloat(_blendXHash, -Mathf.Cos(radian));
            _animator.SetFloat(_blendYHash, Mathf.Sin(radian));
            _animator.SetTrigger(_swingHash);
        }
    }
}
