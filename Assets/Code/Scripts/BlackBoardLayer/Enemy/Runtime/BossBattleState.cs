using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// ボス戦の段階と、今が何周目か、今の足踏みの段階で足踏みした回数、今の行動を保持するステート。
    /// ApplicationLayer（BossBattleService）が書き込み、EngineAdapterLayer（BossBattleAdapter）が読んで攻撃を切り替え、近接切断が停止後の破壊属性の自動付与に読む。
    /// </summary>
    [RegisterBoard(typeof(EnemyBoard))]
    public sealed class BossBattleState : SceneStateBase, IBossBattleState
    {
        public BossBattlePhase Phase { get; private set; } = BossBattlePhase.Idle;
        public int Round { get; private set; }
        public int StompCount { get; private set; }
        public BossAction Action { get; private set; } = BossAction.None;
        public int ActionCount { get; private set; }
        public bool IsStopped => Phase == BossBattlePhase.Stopped;
        public bool IsDefeated { get; private set; }

        public override string GetLog()
        {
            return $"Phase: {Phase}  \nRound: {Round}  \nStompCount: {StompCount}  \nAction: {Action}";
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

        /// <summary>
        /// ボスを倒したことにする。止まったあと、クリアまでの時間が過ぎたときに設定する。
        /// </summary>
        public void SetDefeated()
        {
            IsDefeated = true;
        }

        /// <summary>
        /// 今の行動を設定する。同じ行動を続けて設定しても、新しい指示として数える。
        /// </summary>
        /// <param name="action">ボスの行動</param>
        public void SetAction(BossAction action)
        {
            Action = action;
            ActionCount++;
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

        /// <summary> 今の足踏みの段階で足踏みした回数 </summary>
        int StompCount { get; }

        /// <summary> ボスが止まったか。止まったボスは、武装以外の部位も壊せる </summary>
        bool IsStopped { get; }

        /// <summary> ボスを倒したか。止まったあと、ボスのどこかに攻撃が当たってから決めた時間が過ぎたら true。段階は Stopped のまま </summary>
        bool IsDefeated { get; }

        /// <summary> 今の行動 </summary>
        BossAction Action { get; }

        /// <summary> 行動を設定した回数。同じ行動が続いても、新しい指示かをこの値の変化で見分ける </summary>
        int ActionCount { get; }
    }

    /// <summary>
    /// ボスが一度に1つだけ行う行動。BossBattleService が決め、BossBattleAdapter が実行して、終わったら報告する。
    /// </summary>
    public enum BossAction
    {
        /// <summary> 何もしない。始まる前と停止の間 </summary>
        None,

        /// <summary> 決めた時間だけ何もしない。時間は BossBattleService が数える </summary>
        Wait,

        /// <summary> 水晶の段階の初めに、退く所へ飛び去る </summary>
        Retreat,

        /// <summary> 水晶を投げて置く </summary>
        ThrowCrystals,

        /// <summary> 水晶がすべて壊れるまで、上部タレット・降下フィニッシャー・頭部のレーザーで遠くから攻撃する </summary>
        Barrage,

        /// <summary> 着地点へ飛んでくる </summary>
        Arrive,

        /// <summary> アウトリガーで 1 回足踏みする </summary>
        Stomp,

        /// <summary> 下部レーザーを 1 回撃つ </summary>
        Laser,

        /// <summary> 降下フィニッシャーを 1 つ出す </summary>
        Strike,

        /// <summary> 出した降下フィニッシャーのビームがすべて終わるのを待つ </summary>
        AwaitStrikes,

        /// <summary> 塔を投げる </summary>
        TowerThrow,

        /// <summary> 下部レーザーを撃ち、撃っている間は降下フィニッシャーも撃つ </summary>
        FinalVolley,

        /// <summary> ダウンの間、攻撃が当たるのを待つ。壊せる武装が残っていなければ、行えなかったとして終える </summary>
        AwaitHit,

        /// <summary> 武装を 1 つ壊す </summary>
        BreakWeapon,
    }

    /// <summary>
    /// ボス戦の段階。直接戦闘（Arriving → Stomping → CoreExposed、体力が残れば Stomping へ戻る）で体力が尽きたら Down。
    /// 1 周目の Down のあとは Crystal（遠距離）から 2 周目の直接戦闘へ、2 周目の Down のあとは Final へ進み、Final で体力が尽きたら Stopped。Stopped からは変わらない。
    /// </summary>
    public enum BossBattlePhase
    {
        /// <summary> 始まる前 </summary>
        Idle,

        /// <summary> 遠くへ飛び去って水晶を置き、遠くから攻撃している。体力は無敵。水晶をすべて壊すまで続く </summary>
        Crystal,

        /// <summary> 飛んできている。1 周目はプレイヤーの正面、2 周目は水晶のあった所へ </summary>
        Arriving,

        /// <summary> 足踏みしている </summary>
        Stomping,

        /// <summary> コアを出して、レーザーと降下フィニッシャーで攻撃している </summary>
        CoreExposed,

        /// <summary> 体力が尽きてダウンしている。武装を 1 つ壊すまで続く </summary>
        Down,

        /// <summary> 体力が時間で減っていく間、激しく攻撃している </summary>
        Final,

        /// <summary> 止まった </summary>
        Stopped,
    }
}
