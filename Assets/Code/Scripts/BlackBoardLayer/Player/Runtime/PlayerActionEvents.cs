using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// プレイヤーの出来事の通知を流すチャンネル。PlayerInitializer が1つ作り、PlayerEventBoard へは購読面（IPlayerActionEvents）で登録して、
    /// 発行側の Service には具象を渡す。
    /// </summary>
    public sealed class PlayerActionEvents : IPlayerActionEvents
    {
        private readonly ActionChannel<PlayerActionNotice> _onAction = new();

        public IActionChannel<PlayerActionNotice> OnAction => _onAction;

        /// <summary>
        /// 出来事を購読者へ流す。
        /// </summary>
        /// <param name="notice">流す出来事</param>
        public void Raise(in PlayerActionNotice notice)
        {
            _onAction.Invoke(notice);
        }
    }

    /// <summary>
    /// プレイヤーの出来事の通知の購読面。
    /// </summary>
    public interface IPlayerActionEvents : IEvent
    {
        /// <summary> プレイヤーの出来事（スキル発動、通常攻撃、ジャンプなど）が起きたときに流れる </summary>
        IActionChannel<PlayerActionNotice> OnAction { get; }
    }
}
