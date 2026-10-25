using System.Collections.Generic;
using Kizami.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボス戦の案内の台本。案内の話題ごとに、何をするかの小目標・会話・操作説明の文を持つ。
    /// ボス戦の段階からどの文を出すかの判断は IStageGuideController（StageGuideService）が持ち、台本は文を渡して毎フレーム切り替え時かを尋ねる。
    /// </summary>
    public sealed class BossGuideScript : InitializableMonoBehaviour
    {
        private const string OBJECTIVE = "ボスを倒せ";
        private const string OPERATOR = "オペレーター\n";
        private const string ATTACK_HINT = "[左クリック] 斬撃  [1] / [2] スキル発動";

        private static readonly Dictionary<BossGuideTopic, GuideLine> _lines = new()
        {
            [BossGuideTopic.FirstArrival] = new GuideLine("ボスの体力を削りきれ",
                OPERATOR + "巨大兵器が来る。足元に入り込んで、攻撃を避けながら体力を削れ。", ATTACK_HINT),
            [BossGuideTopic.Return] = new GuideLine("ボスの体力を削りきれ",
                OPERATOR + "ボスが戻ってきた。もう一度、体力を削りきれ。", ATTACK_HINT),
            [BossGuideTopic.Down] = new GuideLine("赤く光る武装を攻撃して壊せ",
                OPERATOR + "ボスが倒れた。今なら武装を壊せる。赤く光っている武装を攻撃しろ。", ATTACK_HINT),
            [BossGuideTopic.Crystal] = new GuideLine("水晶をすべて壊せ",
                OPERATOR + "ボスが距離を取った。今は攻撃が効かない。周りの水晶を壊せ。", "黄色い印の方向へ進め  " + ATTACK_HINT),
            [BossGuideTopic.Final] = new GuideLine("ボスの攻撃を避けて耐えろ",
                OPERATOR + "ボスが最後の攻撃に入った。地面の警告を見て、ビームを避け続けろ。",
                "[W][A][S][D] 移動  [Space] ジャンプ  [Shift] ダッシュ"),
            [BossGuideTopic.Stopped] = new GuideLine("ボスのコアを壊せ",
                OPERATOR + "ボスが止まった。コアを壊せば作戦完了だ。", "黄色い印の方向へ進め  " + ATTACK_HINT),
        };

        private IStageGuideController _controller;

        /// <summary>
        /// 案内の文を操作面へ渡し、大目標を出す。案内の操作面を作ってから呼ぶ。
        /// </summary>
        /// <param name="controller">案内の文を出し、出す文を選ぶ操作面（StageGuideService）</param>
        public void Initialize(IStageGuideController controller)
        {
            _controller = controller;
            _controller.StartBossGuide(OBJECTIVE, _lines);
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized) return;

            _controller.TickBossGuide();
        }
    }
}
