using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 強化型スキルの効果を、強化の種類（EnhanceEffect の派生型）ごとに PlayerEnhanceState へ書くユースケース。
    /// 効果はプレイヤーが決めた回数だけ攻撃すると切れる。同じ種類を重ねて使うと最初から数え直し、違う種類は同時にかかる。
    /// </summary>
    public sealed class PlayerEnhanceService
    {
        private readonly PlayerEnhanceState _state = new();

        /// <param name="blackBoard">PlayerEnhanceState の登録先</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public PlayerEnhanceService(IBlackBoard blackBoard, int sceneId)
        {
            if (!blackBoard.TryGetBoard<PlayerBoard>(out var playerBoard, this)) return;

            playerBoard.RegisterSceneState<IPlayerEnhanceState>(_state, sceneId);
        }

        /// <summary>
        /// スキルの強化を、最初から数え直してかける。
        /// </summary>
        /// <param name="effect">かける強化型スキルの効果</param>
        public void Begin(EnhanceEffect effect)
        {
            _state.Set(effect.GetType(), effect.Scale, effect.Count);
        }

        /// <summary>
        /// 強化がかかっているかを返す。回数は使わない。
        /// </summary>
        /// <typeparam name="T">強化の種類</typeparam>
        /// <param name="scale">かかっていれば強化の倍率、かかっていなければ1</param>
        public bool IsActive<T>(out float scale) where T : EnhanceEffect
        {
            return _state.TryGet(typeof(T), out scale, out _);
        }

        /// <summary>
        /// 攻撃を1回行うときに呼び、強化がかかっていれば残りを1減らす。
        /// </summary>
        /// <typeparam name="T">強化の種類</typeparam>
        /// <param name="scale">かかっていれば強化の倍率、かかっていなければ1</param>
        /// <returns>この攻撃に強化がかかるなら true</returns>
        public bool TryUse<T>(out float scale) where T : EnhanceEffect
        {
            if (!_state.TryGet(typeof(T), out scale, out var remainingCount)) return false;

            _state.SetRemainingCount(typeof(T), remainingCount - 1);
            return true;
        }
    }
}
