using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 崩壊とチュートリアルのステージの目標を選ぶ。クリアに数える破壊対象のうち、まだ破壊済みでなく、出ているものを候補にする。ステージシーンへ置く。
    /// </summary>
    public sealed class DestructionTargetSelector : TargetSelectorBase
    {
        protected override void CollectCandidates(List<Vector3> candidates)
        {
            foreach (var target in ClearTargets)
            {
                // 破棄された Unity のオブジェクトは、インターフェース越しでは == null で見分けられない
                if (target is not Component component || component == null || target.IsDestroyed ||
                    !component.gameObject.activeInHierarchy) continue;

                candidates.Add(target.Center);
            }
        }
    }
}
