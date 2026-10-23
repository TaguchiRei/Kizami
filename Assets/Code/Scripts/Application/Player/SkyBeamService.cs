using System;
using Kizami.BlackBoard;
using Kizami.External;

namespace Kizami.Application
{
    /// <summary>
    /// 羽天轟号の使用中の流れを持つユースケース。使い始めてから、止めて地上へ戻り終えるまでの間、プレイヤーを静止させて（無敵）、通常攻撃とほかのスキルを出せなくする。
    /// 照射中の判定のたびにチャージを使い、足りなければ止める。
    /// 時間の進行（上昇・チャージ・判定の間隔・戻り）は実行役が持ち、判定の前に TryContinue で続けてよいかを尋ね、戻り終えたら OnFinished で知らせる。
    /// </summary>
    public sealed class SkyBeamService
    {
        private readonly PlayerMovementService _movementService;
        private readonly ChargeService _chargeService;
        private readonly PlayerActionEvents _actionEvents;
        private readonly Action<float, float, float, float, float> _onBegin;
        private readonly Action _onStop;

        /// <summary> 判定 1 回ごとに使うチャージ量。使い始めたスキルの値 </summary>
        private int _sustainCost;

        private bool _isStopping;

        /// <summary> 使い始めてから戻り終えるまでか </summary>
        public bool IsActive { get; private set; }

        /// <summary> 使い始められるか。使用中と、言葉をつかんでいる間は false </summary>
        public bool CanBegin => !IsActive && !_movementService.IsHoldingWord;

        /// <param name="movementService">使用中に静止させる移動の Service</param>
        /// <param name="chargeService">判定ごとのチャージの消費先</param>
        /// <param name="actionEvents">判定ごとのチャージの消費を流す先</param>
        /// <param name="onBegin">上昇を始める関数。引数は上昇先のワールド高度（Y）、上昇と戻りの所要時間（秒）、判定の間隔（秒）、判定で壊す球の半径（m）、体力制の対象の体力を 1 秒あたりに減らす量</param>
        /// <param name="onStop">止めて、発動した地点へ戻る関数</param>
        public SkyBeamService(PlayerMovementService movementService, ChargeService chargeService,
            PlayerActionEvents actionEvents, Action<float, float, float, float, float> onBegin, Action onStop)
        {
            _movementService = movementService;
            _chargeService = chargeService;
            _actionEvents = actionEvents;
            _onBegin = onBegin;
            _onStop = onStop;
        }

        /// <summary>
        /// 使い始める。使い始められないときは何もしない。
        /// </summary>
        public void Begin(SkyBeamEffect effect)
        {
            if (!CanBegin) return;

            IsActive = true;
            _isStopping = false;
            _sustainCost = effect.SustainCost;
            _movementService.SetSkyBeamActive(true);
            _onBegin?.Invoke(effect.Altitude, effect.RiseDuration, effect.SustainInterval, effect.Radius,
                effect.HealthDamagePerSecond);
        }

        /// <summary>
        /// 判定を 1 回行ってよいかを返す。判定 1 回分のチャージを使えたら、消費を通知して true。使えなければ止めて false。
        /// </summary>
        public bool TryContinue()
        {
            if (!IsActive || _isStopping) return false;

            if (!_chargeService.TryConsume(_sustainCost))
            {
                Stop();
                return false;
            }

            _actionEvents?.Raise(new PlayerActionNotice(PlayerActionKind.ChargeSpent, amount: _sustainCost));
            return true;
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
