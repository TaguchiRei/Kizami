using System.Collections.Generic;
using Kizami.BlackBoard;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボス戦で、プレイヤーが次に向かう場所を選ぶ。水晶の段階は残っている水晶、ボスが止まったあとはまだ壊れていないクリアの破壊対象（コア）を候補にする。
    /// それ以外の段階では目標がない。ボス戦の配線のときに実行時に作る。
    /// </summary>
    public sealed class BossTargetSelector : TargetSelectorBase
    {
        private IBossBattleState _state;
        private BossCrystals _crystals;

        /// <summary>
        /// 候補を出すのに使う物を受け取る。
        /// </summary>
        /// <param name="state">ボス戦の State</param>
        /// <param name="crystals">水晶の段階で投げる水晶。なければ水晶の段階には目標がない</param>
        public void Connect(IBossBattleState state, BossCrystals crystals)
        {
            _state = state;
            _crystals = crystals;
        }

        protected override void CollectCandidates(List<Vector3> candidates)
        {
            if (_state == null) return;

            switch (_state.Phase)
            {
                case BossBattlePhase.Crystal:
                    if (_crystals != null) _crystals.AppendRemainingCenters(candidates);
                    break;
                case BossBattlePhase.Stopped:
                    foreach (var target in ClearTargets)
                    {
                        // 破棄された Unity のオブジェクトは、インターフェース越しでは == null で見分けられない
                        if (target is not Component component || component == null || target.IsDestroyed) continue;

                        candidates.Add(target.Center);
                    }

                    break;
            }
        }
    }
}
