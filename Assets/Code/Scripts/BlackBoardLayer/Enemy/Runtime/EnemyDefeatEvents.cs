using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 敵を倒した通知を流すチャンネル。EnemyInitializer が1つ作り、EnemyEventBoard へは購読面（IEnemyDefeatEvents）で登録して、
    /// 発行側の EnemySpawnAdapter には Raise を渡す。
    /// </summary>
    public sealed class EnemyDefeatEvents : IEnemyDefeatEvents
    {
        private readonly ActionChannel<EnemyDefeatNotice> _onDefeated = new();

        public IActionChannel<EnemyDefeatNotice> OnDefeated => _onDefeated;

        /// <summary>
        /// 倒した通知を購読者へ流す。
        /// </summary>
        /// <param name="notice">倒した敵の通知</param>
        public void Raise(EnemyDefeatNotice notice)
        {
            _onDefeated.Invoke(notice);
        }
    }

    /// <summary>
    /// 敵を倒した通知の購読面。
    /// </summary>
    public interface IEnemyDefeatEvents : IEvent
    {
        /// <summary> 敵を倒したときに流れる。同じ原因で同時に倒した敵は 1 回にまとめる </summary>
        IActionChannel<EnemyDefeatNotice> OnDefeated { get; }
    }
}
