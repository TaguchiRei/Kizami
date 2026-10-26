using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// Brilliant Skill の結果をエンジン側で実行させる出来事を流すチャンネル。StageInitializer が1つ作り、StageEventBoard へは購読面（IBrilliantSkillEvents）で登録して、
    /// 発行側の BrilliantSkillService には具象を渡す。
    /// </summary>
    public sealed class BrilliantSkillEvents : IBrilliantSkillEvents
    {
        private readonly ActionChannel<SphereCarveNotice> _onWordImpact = new();
        private readonly ActionChannel<string> _onTriggered = new();

        public IActionChannel<SphereCarveNotice> OnWordImpact => _onWordImpact;
        public IActionChannel<string> OnTriggered => _onTriggered;

        /// <summary>
        /// 投げた言葉の着弾で、球の中のボクセルを削り、敵を倒させる。
        /// </summary>
        public void RaiseWordImpact(in SphereCarveNotice notice)
        {
            _onWordImpact.Invoke(notice);
        }

        /// <summary>
        /// 成立した Brilliant Skill の言葉を出させる。
        /// </summary>
        /// <param name="word">出す言葉</param>
        public void RaiseTriggered(string word)
        {
            _onTriggered.Invoke(word);
        }
    }

    /// <summary>
    /// Brilliant Skill の結果を実行させる出来事の購読面。
    /// </summary>
    public interface IBrilliantSkillEvents : IEvent
    {
        /// <summary> 投げた言葉が着弾したときに流れる。球の中のボクセルを削り、敵を倒す。登録した順に呼ぶ為、削る側を先に登録する </summary>
        IActionChannel<SphereCarveNotice> OnWordImpact { get; }

        /// <summary> Brilliant Skill が成立したときに流れる。値は出す言葉。同時に成立した種類の数だけ続けて流れる </summary>
        IActionChannel<string> OnTriggered { get; }
    }
}
