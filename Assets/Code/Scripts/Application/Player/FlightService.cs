using System;
using Kizami.BlackBoard;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 視線の先の飛び移りの目印を受け取って F キーの誘導を出し、F が押されたときに目印へ飛ぶユースケース。
    /// F は普段スローモードの入力なので、飛び移れるときだけ先にここで受け取る。
    /// </summary>
    public sealed class FlightService
    {
        private readonly FlightPromptState _state = new();
        private readonly Func<bool> _canFly;
        private readonly Action<FlightTarget> _onFly;

        private FlightTarget? _candidate;

        /// <param name="blackBoard">FlightPromptState の登録先</param>
        /// <param name="canFly">飛び始められるかを返す関数</param>
        /// <param name="onFly">目印へ飛ぶ関数</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public FlightService(IBlackBoard blackBoard, Func<bool> canFly, Action<FlightTarget> onFly, int sceneId)
        {
            _canFly = canFly;
            _onFly = onFly;

            if (!blackBoard.TryGetBoard<PlayerBoard>(out var playerBoard, this)) return;

            playerBoard.RegisterSceneState<IFlightPromptState>(_state, sceneId);
        }

        /// <summary>
        /// 視線の先の目印を設定する。毎フレーム、見つけた目印か null を渡す。
        /// </summary>
        public void SetCandidate(FlightTarget? candidate)
        {
            _candidate = candidate;
            _state.SetVisible(candidate.HasValue);
        }

        /// <summary>
        /// 視線の先に目印があり、飛び始められるなら飛ぶ。
        /// </summary>
        /// <returns>飛んだら true。飛ばなかったときは false で、F は他の入力として扱う</returns>
        public bool TryFly()
        {
            if (!_candidate.HasValue || !_canFly()) return false;

            _onFly(_candidate.Value);
            return true;
        }
    }
}
