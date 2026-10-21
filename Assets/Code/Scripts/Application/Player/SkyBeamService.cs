using System;
using Kizami.External;

namespace Kizami.Application
{
    /// <summary>
    /// 羽天轟号の使用中の流れを持つユースケース。使い始めてから、止めて地上へ戻り終えるまでの間、プレイヤーを静止させて（無敵）、通常攻撃とほかのスキルを出せなくする。
    /// 時間の進行（上昇・戻り）は実行役が持ち、戻り終えたら OnFinished で知らせる。
    /// </summary>
    public sealed class SkyBeamService
    {
        private readonly PlayerMovementService _movementService;
        private readonly Action<float, float> _onBegin;
        private readonly Action _onStop;

        private bool _isStopping;

        /// <summary> 使い始めてから戻り終えるまでか </summary>
        public bool IsActive { get; private set; }

        /// <summary> 使い始められるか。使用中と、言葉をつかんでいる間は false </summary>
        public bool CanBegin => !IsActive && !_movementService.IsHoldingWord;

        /// <param name="movementService">使用中に静止させる移動の Service</param>
        /// <param name="onBegin">上昇を始める関数。引数は上昇先のワールド高度（Y）、上昇と戻りの所要時間（秒）</param>
        /// <param name="onStop">止めて、発動した地点へ戻る関数</param>
        public SkyBeamService(PlayerMovementService movementService, Action<float, float> onBegin, Action onStop)
        {
            _movementService = movementService;
            _onBegin = onBegin;
            _onStop = onStop;
        }

        /// <summary>
        /// 使い始める。使い始められないときは何もしない。
        /// </summary>
        public void Begin(SkillData skill)
        {
            if (!CanBegin) return;

            IsActive = true;
            _isStopping = false;
            _movementService.SetSkyBeamActive(true);
            _onBegin?.Invoke(skill.Altitude, skill.RiseDuration);
        }

        /// <summary>
        /// 止めて、発動した地点へ戻り始める。使用中でないときと、もう戻り始めているときは何もしない。
        /// </summary>
        public void Stop()
        {
            if (!IsActive || _isStopping) return;

            _isStopping = true;
            _onStop?.Invoke();
        }

        /// <summary>
        /// 戻り終えたことを受け取り、静止を解いて使用中を終える。
        /// </summary>
        public void OnFinished()
        {
            IsActive = false;
            _isStopping = false;
            _movementService.SetSkyBeamActive(false);
        }
    }
}
