using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボス戦の案内の台本。ボス戦の段階が切り替わるたびに、その段階で何をするかの小目標・会話・操作説明を出す。
    /// 直接戦闘の中の飛来・足踏み・コアの露出は、やることが同じなので1つの案内にまとめる。
    /// 画面に出す文は、初期化で受け取った関数で Application へ渡す。
    /// </summary>
    public sealed class BossGuideScript : InitializableMonoBehaviour
    {
        private const string OBJECTIVE = "ボスを倒せ";
        private const string OPERATOR = "オペレーター\n";
        private const string ATTACK_HINT = "[左クリック] 斬撃  [1] / [2] スキル発動";

        private IBossBattleState _state;
        private Action<string> _setObjective;
        private Action<string> _setSubObjective;
        private Action<string> _setDialogue;
        private Action<string> _setHint;

        /// <summary> 案内を出した段階のまとまり。まだ出していなければ Idle </summary>
        private BossBattlePhase _shownPhase = BossBattlePhase.Idle;

        /// <summary> 案内を出したときの周の番号 </summary>
        private int _shownRound;

        /// <summary>
        /// ボス戦の State と、案内の文を画面に出す関数を受け取る。ボス戦の State の登録より後に呼ぶ。
        /// </summary>
        /// <param name="state">ボス戦の State</param>
        /// <param name="setObjective">大目標を画面に出す関数</param>
        /// <param name="setSubObjective">小目標を画面に出す関数</param>
        /// <param name="setDialogue">会話を画面に出す関数</param>
        /// <param name="setHint">操作説明を画面に出す関数</param>
        public void Initialize(IBossBattleState state, Action<string> setObjective, Action<string> setSubObjective,
            Action<string> setDialogue, Action<string> setHint)
        {
            _state = state;
            _setObjective = setObjective;
            _setSubObjective = setSubObjective;
            _setDialogue = setDialogue;
            _setHint = setHint;
            _setObjective?.Invoke(OBJECTIVE);
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized || _state == null) return;

            var phase = ToGuidePhase(_state.Phase);
            if (phase == BossBattlePhase.Idle || (phase == _shownPhase && _state.Round == _shownRound)) return;

            _shownPhase = phase;
            _shownRound = _state.Round;
            Show(phase, _state.Round);
        }

        /// <summary>
        /// 案内を分ける段階のまとまりに直す。直接戦闘の3つの段階は Arriving にまとめる。
        /// </summary>
        private static BossBattlePhase ToGuidePhase(BossBattlePhase phase)
        {
            return phase is BossBattlePhase.Stomping or BossBattlePhase.CoreExposed ? BossBattlePhase.Arriving : phase;
        }

        private void Show(BossBattlePhase phase, int round)
        {
            switch (phase)
            {
                case BossBattlePhase.Arriving:
                    Set("ボスの体力を削りきれ",
                        round <= 1
                            ? OPERATOR + "巨大兵器が来る。足元に入り込んで、攻撃を避けながら体力を削れ。"
                            : OPERATOR + "ボスが戻ってきた。もう一度、体力を削りきれ。",
                        ATTACK_HINT);
                    break;
                case BossBattlePhase.Down:
                    Set("赤く光る武装を攻撃して壊せ",
                        OPERATOR + "ボスが倒れた。今なら武装を壊せる。赤く光っている武装を攻撃しろ。",
                        ATTACK_HINT);
                    break;
                case BossBattlePhase.Crystal:
                    Set("水晶をすべて壊せ",
                        OPERATOR + "ボスが距離を取った。今は攻撃が効かない。周りの水晶を壊せ。",
                        "黄色い印の方向へ進め  " + ATTACK_HINT);
                    break;
                case BossBattlePhase.Final:
                    Set("ボスの攻撃を避けて耐えろ",
                        OPERATOR + "ボスが最後の攻撃に入った。地面の警告を見て、ビームを避け続けろ。",
                        "[W][A][S][D] 移動  [Space] ジャンプ  [Shift] ダッシュ");
                    break;
                case BossBattlePhase.Stopped:
                    Set("ボスのコアを壊せ",
                        OPERATOR + "ボスが止まった。コアを壊せば作戦完了だ。",
                        "黄色い印の方向へ進め  " + ATTACK_HINT);
                    break;
            }
        }

        private void Set(string subObjective, string dialogue, string hint)
        {
            _setSubObjective?.Invoke(subObjective);
            _setDialogue?.Invoke(dialogue);
            _setHint?.Invoke(hint);
        }
    }
}
