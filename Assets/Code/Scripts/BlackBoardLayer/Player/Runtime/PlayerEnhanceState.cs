using System;
using System.Collections.Generic;
using System.Linq;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// かかっている強化の倍率と残り回数を、強化の種類（EnhanceEffect の派生型）ごとに保持するステート。
    /// 残り回数が0になった強化は持たない。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class PlayerEnhanceState : SceneStateBase, IPlayerEnhanceState
    {
        private readonly Dictionary<Type, (float Scale, int RemainingCount)> _enhancements = new();
        private readonly ActionEntryList<Type, int> _enhanceChangedActions = new();

        public override string GetLog()
        {
            return _enhancements.Count == 0
                ? "Enhance: -"
                : "Enhance: " + string.Join(", ", _enhancements.Select(pair => $"{pair.Key.Name} x{pair.Value.RemainingCount}"));
        }

        /// <summary>
        /// 強化をかける。同じ種類がかかっていれば置き換える。変化の通知を流す。
        /// </summary>
        /// <param name="enhanceType">強化の種類</param>
        /// <param name="scale">強化の倍率</param>
        /// <param name="remainingCount">残り回数。1以上</param>
        public void Set(Type enhanceType, float scale, int remainingCount)
        {
            _enhancements[enhanceType] = (scale, remainingCount);
            _enhanceChangedActions.Invoke(enhanceType, remainingCount);
        }

        /// <summary>
        /// かかっている強化の残り回数を設定する。0以下なら強化を外す。かかっていない種類は何もしない。変化の通知を流す。
        /// </summary>
        /// <param name="enhanceType">強化の種類</param>
        /// <param name="remainingCount">残り回数</param>
        public void SetRemainingCount(Type enhanceType, int remainingCount)
        {
            if (!_enhancements.TryGetValue(enhanceType, out var enhancement)) return;

            if (remainingCount <= 0)
            {
                _enhancements.Remove(enhanceType);
                remainingCount = 0;
            }
            else
            {
                _enhancements[enhanceType] = (enhancement.Scale, remainingCount);
            }

            _enhanceChangedActions.Invoke(enhanceType, remainingCount);
        }

        public bool TryGet(Type enhanceType, out float scale, out int remainingCount)
        {
            if (_enhancements.TryGetValue(enhanceType, out var enhancement))
            {
                scale = enhancement.Scale;
                remainingCount = enhancement.RemainingCount;
                return true;
            }

            scale = 1f;
            remainingCount = 0;
            return false;
        }

        public IDisposable RegisterOnEnhanceChanged(Action<Type, int> callback)
        {
            return _enhanceChangedActions.Register(new ActionEntry<Type, int>(false, callback), nameof(callback));
        }
    }

    /// <summary>
    /// かかっている強化の読み取り面。
    /// </summary>
    public interface IPlayerEnhanceState : IStateGetter
    {
        /// <summary>
        /// 強化がかかっているかと、その倍率と残り回数を取得する。
        /// </summary>
        /// <param name="enhanceType">強化の種類（EnhanceEffect の派生型）</param>
        /// <param name="scale">かかっていれば強化の倍率、かかっていなければ1</param>
        /// <param name="remainingCount">かかっていれば残り回数、かかっていなければ0</param>
        /// <returns>強化がかかっていれば true</returns>
        bool TryGet(Type enhanceType, out float scale, out int remainingCount);

        /// <summary>
        /// 強化がかかったとき、残り回数が変わったとき、切れたときに発火するイベントを登録する。
        /// </summary>
        /// <param name="callback">変化時に実行する処理。引数に強化の種類と変化後の残り回数が入る。切れたときの残り回数は0</param>
        IDisposable RegisterOnEnhanceChanged(Action<Type, int> callback);
    }
}
