using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 強化型スキルの効果を、強化の種類（EnhanceKind）ごとに PlayerEnhanceState へ書くユースケース。
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
            _state.Set(ToKind(effect), effect.Scale, effect.Count);
        }

        /// <summary>
        /// 強化がかかっているかを返す。回数は使わない。
        /// </summary>
        /// <param name="kind">強化の種類</param>
        /// <param name="scale">かかっていれば強化の倍率、かかっていなければ1</param>
        public bool IsActive(EnhanceKind kind, out float scale)
        {
            return _state.TryGet(kind, out scale, out _);
        }

        /// <summary>
        /// 攻撃を1回行うときに呼び、強化がかかっていれば残りを1減らす。
        /// </summary>
        /// <param name="kind">強化の種類</param>
        /// <param name="scale">かかっていれば強化の倍率、かかっていなければ1</param>
        /// <returns>この攻撃に強化がかかるなら true</returns>
        public bool TryUse(EnhanceKind kind, out float scale)
        {
            if (!_state.TryGet(kind, out scale, out var remainingCount)) return false;

            _state.SetRemainingCount(kind, remainingCount - 1);
            return true;
        }

        private static EnhanceKind ToKind(EnhanceEffect effect)
        {
            return effect switch
            {
                DestroyEnhanceEffect => EnhanceKind.Destroy,
                ArmorBreakEnhanceEffect => EnhanceKind.ArmorBreak,
                RangeEnhanceEffect => EnhanceKind.Range,
                SpeedEnhanceEffect => EnhanceKind.Speed,
                ExplosiveFragmentEnhanceEffect => EnhanceKind.ExplosiveFragment,
                _ => throw new System.ArgumentOutOfRangeException(nameof(effect), effect.GetType().Name,
                    "EnhanceKind に対応していない強化です。")
            };
        }
    }
}
