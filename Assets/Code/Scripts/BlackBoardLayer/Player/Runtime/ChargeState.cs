using System;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// チャージ量を保持するステート。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class ChargeState : SceneStateBase, IChargeState
    {
        private readonly ActionEntryList<int, int> _chargeChangedActions = new();

        public int Current { get; private set; }
        public int Max { get; }

        /// <param name="max">チャージ量の上限。現在のチャージ量は0から始まる</param>
        public ChargeState(int max)
        {
            Max = max;
        }

        public override string GetLog()
        {
            return $"Charge: {Current} / {Max}";
        }

        /// <summary>
        /// 現在のチャージ量を設定する。値は0〜上限に収める。値が変わったときだけ変化の通知を流す。
        /// </summary>
        /// <param name="current">現在のチャージ量</param>
        public void SetCurrent(int current)
        {
            current = Math.Clamp(current, 0, Max);
            if (Current == current) return;

            var previous = Current;
            Current = current;
            _chargeChangedActions.Invoke(previous, current);
        }

        public IDisposable RegisterOnChargeChanged(Action<int, int> callback)
        {
            return _chargeChangedActions.Register(new ActionEntry<int, int>(false, callback), nameof(callback));
        }
    }

    /// <summary>
    /// チャージ量の読み取り面。
    /// </summary>
    public interface IChargeState : IStateGetter
    {
        /// <summary> 現在のチャージ量 </summary>
        int Current { get; }

        /// <summary> チャージ量の上限 </summary>
        int Max { get; }

        /// <summary>
        /// チャージ量が変化した際に発火するイベントを登録する。
        /// </summary>
        /// <param name="callback">変化時に実行する処理。引数に変化前と変化後の値が入る</param>
        IDisposable RegisterOnChargeChanged(Action<int, int> callback);
    }
}
