using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// チャージ量を管理するユースケース。
    /// 吸収したかけらの数に、かけら 1 個あたりのチャージ量を掛けて加える。
    /// </summary>
    public sealed class ChargeService
    {
        private readonly ChargeState _state;
        private readonly int _chargePerFragment;

        /// <param name="blackBoard">ChargeState の登録先</param>
        /// <param name="parameters">チャージ量の上限と、かけら 1 個あたりのチャージ量の取得元</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public ChargeService(IBlackBoard blackBoard, PlayerParameterData parameters, int sceneId)
        {
            _state = new ChargeState(parameters.MaxCharge);
            _chargePerFragment = parameters.ChargePerFragment;

            if (!blackBoard.TryGetBoard<PlayerBoard>(out var playerBoard, this)) return;

            playerBoard.RegisterSceneState<IChargeState>(_state, sceneId);
        }

        /// <summary>
        /// 吸収したかけらの数だけチャージ量を加える。上限を超えた分は捨てる。
        /// </summary>
        /// <param name="count">吸収したかけらの数</param>
        public void AddFragments(int count)
        {
            if (count <= 0) return;

            _state.SetCurrent(_state.Current + count * _chargePerFragment);
        }
    }
}
