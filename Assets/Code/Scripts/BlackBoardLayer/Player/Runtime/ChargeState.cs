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
        public int Current { get; private set; }
        public int Max { get; }

        /// <param name="max">チャージ量の上限。現在のチャージ量は 0 から始まる</param>
        public ChargeState(int max)
        {
            Max = max;
        }

        public override string GetLog()
        {
            return $"Charge: {Current} / {Max}";
        }

        /// <summary>
        /// 現在のチャージ量を設定する。値は 0〜上限に収める。
        /// </summary>
        /// <param name="current">現在のチャージ量</param>
        public void SetCurrent(int current)
        {
            Current = Math.Clamp(current, 0, Max);
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
    }
}
