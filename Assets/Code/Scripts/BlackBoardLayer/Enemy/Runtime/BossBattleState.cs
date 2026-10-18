using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// ボス戦の段階と、今が何周目か、今の周で足踏みした回数を保持するステート。
    /// ApplicationLayer（BossBattleService）が書き込み、EngineAdapterLayer（BossBattleAdapter）が読んで攻撃を切り替え、近接切断が停止後の破壊属性の自動付与に読む。
    /// </summary>
    [RegisterBoard(typeof(EnemyBoard))]
    public sealed class BossBattleState : SceneStateBase, IBossBattleState
    {
        public BossBattlePhase Phase { get; private set; } = BossBattlePhase.Idle;
        public int Round { get; private set; }
        public int StompCount { get; private set; }
        public bool IsStopped => Phase == BossBattlePhase.Stopped;

        public override string GetLog()
        {
            return $"Phase: {Phase}  \nRound: {Round}  \nStompCount: {StompCount}";
        }

        /// <summary>
        /// 段階を設定する。
        /// </summary>
        /// <param name="phase">ボス戦の段階</param>
        public void SetPhase(BossBattlePhase phase)
        {
            Phase = phase;
        }

        /// <summary>
        /// 今が何周目かを設定する。
        /// </summary>
        /// <param name="round">周の番号（1から）</param>
        public void SetRound(int round)
        {
            Round = round;
        }

        /// <summary>
        /// 今の周で足踏みした回数を設定する。
        /// </summary>
        /// <param name="stompCount">足踏みした回数</param>
        public void SetStompCount(int stompCount)
        {
            StompCount = stompCount;
        }
    }

    /// <summary>
    /// ボス戦の読み取り面。
    /// </summary>
    public interface IBossBattleState : IStateGetter
    {
        /// <summary> ボス戦の段階 </summary>
        BossBattlePhase Phase { get; }

        /// <summary> 今が何周目か。始まる前は 0 </summary>
        int Round { get; }

        /// <summary> 今の周で足踏みした回数 </summary>
        int StompCount { get; }

        /// <summary> ボスが止まったか。止まったボスは、武装以外の部位も壊せる </summary>
        bool IsStopped { get; }
    }

    /// <summary>
    /// ボス戦の段階。1 周は Crystal → Arriving → Stomping → CoreExposed → Opening。Stopped からは変わらない。
    /// </summary>
    public enum BossBattlePhase
    {
        /// <summary> 始まる前 </summary>
        Idle,

        /// <summary> 水晶を置き、遠くから攻撃している。水晶をすべて壊すまで続く </summary>
        Crystal,

        /// <summary> 水晶のあった所へ飛んできている </summary>
        Arriving,

        /// <summary> 足踏みしている </summary>
        Stomping,

        /// <summary> コアを出して、レーザーとミサイルで攻撃している </summary>
        CoreExposed,

        /// <summary> 隙を晒している。武装を 1 つ壊すまで続く </summary>
        Opening,

        /// <summary> 総攻撃をしている </summary>
        AllOutAttack,

        /// <summary> 止まった </summary>
        Stopped,
    }
}
