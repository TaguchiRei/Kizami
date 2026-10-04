using System;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// プレイヤーの HP を保持するステート。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class PlayerHealthState : SceneStateBase, IPlayerHealthState
    {
        public int Current { get; private set; }
        public int Max { get; private set; }

        private Action<int, int> _healthChangedCallback;

        /// <param name="max">最大 HP。現在の HP もこの値から始まる</param>
        public PlayerHealthState(int max)
        {
            Max = max;
            Current = max;
        }

        /// <summary>
        /// 現在の HP を設定する。値は 0〜最大 HP に収める。値が変わったときだけ変化の通知を流す。
        /// </summary>
        /// <param name="current">現在の HP</param>
        public void SetCurrent(int current)
        {
            current = Math.Clamp(current, 0, Max);
            if (Current == current) return;

            var previous = Current;
            Current = current;
            _healthChangedCallback?.Invoke(previous, current);
        }

        public IDisposable RegisterOnHealthChanged(Action<int, int> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            _healthChangedCallback += callback;
            return new BoardDispose(() => _healthChangedCallback -= callback);
        }

        public override string GetLog()
        {
            return $"HP: {Current} / {Max}";
        }
    }

    /// <summary>
    /// プレイヤーの HP の読み取り面。
    /// </summary>
    public interface IPlayerHealthState : IStateGetter
    {
        /// <summary> 現在の HP </summary>
        int Current { get; }

        /// <summary> 最大 HP </summary>
        int Max { get; }

        /// <summary>
        /// 現在の HP が変化した際に発火するイベントを登録する。
        /// </summary>
        /// <param name="callback">変化時に実行する処理。引数に変化前と変化後の値が入る</param>
        IDisposable RegisterOnHealthChanged(Action<int, int> callback);
    }
}
