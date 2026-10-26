using System;
using System.Collections.Generic;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// ステージの案内の操作面。StageGuideService が実装し、台本（StageGuideScript・BossGuideScript）が使う。
    /// 台本は段階と文の定義を渡し、エンジン側の観測を報告する。段階を進める判断と、ボス戦で出す文を選ぶ判断は実装側が持つ。
    /// </summary>
    public interface IStageGuideController
    {
        /// <summary>
        /// 大目標を設定する。
        /// </summary>
        /// <param name="objective">大目標の文。空なら出さない</param>
        void SetObjective(string objective);

        /// <summary>
        /// 小目標を設定する。
        /// </summary>
        /// <param name="subObjective">小目標の文。空なら出さない</param>
        void SetSubObjective(string subObjective);

        /// <summary>
        /// 会話を設定する。
        /// </summary>
        /// <param name="dialogue">会話の文。空なら出さない</param>
        void SetDialogue(string dialogue);

        /// <summary>
        /// 操作説明を設定する。
        /// </summary>
        /// <param name="hint">操作説明の文。空なら出さない</param>
        void SetHint(string hint);

        /// <summary>
        /// 台本の段階を受け取る。プレイ中になった最初の TickScript で段階 0 に入る。
        /// </summary>
        /// <param name="steps">段階を終える条件と待ち時間。上から順に進む</param>
        /// <param name="clearsOnFinish">最後の段階を終えたときに、ステージをクリアにするか</param>
        /// <param name="enterStep">段階に入ったときに呼ぶ関数。引数は段階の番号で、全部終えたら段階の数</param>
        void StartScript(IReadOnlyList<GuideStepRule> steps, bool clearsOnFinish, Action<int> enterStep);

        /// <summary>
        /// 毎フレーム呼ぶ。今の段階の条件を満たし、待ち時間が経ったら次の段階へ進める。1回で進むのは1段階まで。プレイ中のときだけ進める。
        /// </summary>
        void TickScript();

        /// <summary>
        /// 今の段階で待っている破壊対象が破壊済みになったことを報告する。
        /// </summary>
        void ReportTargetDestroyed();

        /// <summary>
        /// ボス戦の案内の文を受け取り、大目標を出す。以降の TickBossGuide で、ボス戦の段階が切り替わるたびに文を出す。
        /// </summary>
        /// <param name="objective">大目標の文</param>
        /// <param name="lines">案内の話題ごとの文。ない話題は出さない</param>
        void StartBossGuide(string objective, IReadOnlyDictionary<BossGuideTopic, GuideLine> lines);

        /// <summary>
        /// 毎フレーム呼ぶ。ボス戦の段階か周が切り替わっていたら、その段階の文を出す。
        /// </summary>
        void TickBossGuide();
    }

    /// <summary>
    /// 台本の段階を終える条件の種類。
    /// </summary>
    public enum GuideConditionKind
    {
        /// <summary> 指定した破壊対象が破壊済みになった </summary>
        TargetDestroyed,

        /// <summary> この段階に入ってから、指定した数の敵を倒した </summary>
        DefeatCount,

        /// <summary> この段階に入ってから、指定したプレイヤーの出来事が起きた </summary>
        PlayerAction,

        /// <summary> プレイヤーが移動の入力をしている </summary>
        PlayerMoved,

        /// <summary> 条件はなく、段階に入ってすぐ満たす。待ち時間だけで進める </summary>
        None,

        /// <summary> クリアの対象のうち、どれでもよいので、指定した数が破壊済みになった。ステージを始めてからの数で数える </summary>
        DestroyedTargetCount
    }

    /// <summary>
    /// 台本の段階1つを終える条件と待ち時間。
    /// </summary>
    public readonly struct GuideStepRule
    {
        /// <summary> 条件の種類 </summary>
        public GuideConditionKind Kind { get; }

        /// <summary> 倒す敵の数か、破壊済みを待つ対象の数。1 未満は 1 として扱う </summary>
        public int Count { get; }

        /// <summary> 待つプレイヤーの出来事 </summary>
        public PlayerActionKind Action { get; }

        /// <summary> 条件を満たしてから次の段階へ進むまでの待ち時間（秒） </summary>
        public float Delay { get; }

        public GuideStepRule(GuideConditionKind kind, int count, PlayerActionKind action, float delay)
        {
            Kind = kind;
            Count = count;
            Action = action;
            Delay = delay;
        }
    }

    /// <summary>
    /// ボス戦の案内を分ける話題。
    /// </summary>
    public enum BossGuideTopic
    {
        /// <summary> 1 周目の直接戦闘（飛来・足踏み・コアの露出） </summary>
        FirstArrival,

        /// <summary> 2 周目以降の直接戦闘 </summary>
        Return,

        /// <summary> ダウン </summary>
        Down,

        /// <summary> 水晶 </summary>
        Crystal,

        /// <summary> 最終の攻撃 </summary>
        Final,

        /// <summary> 停止 </summary>
        Stopped
    }

    /// <summary>
    /// 案内で一度に出す小目標・会話・操作説明の文。
    /// </summary>
    public readonly struct GuideLine
    {
        /// <summary> 小目標の文 </summary>
        public string SubObjective { get; }

        /// <summary> 会話の文 </summary>
        public string Dialogue { get; }

        /// <summary> 操作説明の文 </summary>
        public string Hint { get; }

        public GuideLine(string subObjective, string dialogue, string hint)
        {
            SubObjective = subObjective;
            Dialogue = dialogue;
            Hint = hint;
        }
    }
}
