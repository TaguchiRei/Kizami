using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 崩壊のステージの目標を選ぶ。クリアに数える破壊対象のうち、まだ破壊済みでないものを候補にする。ステージシーンへ置く。
    /// </summary>
    public sealed class DestructionTargetSelector : TargetSelectorBase
    {
        protected override void CollectCandidates(List<Vector3> candidates)
        {
            foreach (var target in ClearTargets)
            {
                // 破棄された Unity のオブジェクトは、インターフェース越しでは == null で見分けられない
                if (target is not Component component || component == null || target.IsDestroyed) continue;

                candidates.Add(target.Center);
            }
        }
    }
}
