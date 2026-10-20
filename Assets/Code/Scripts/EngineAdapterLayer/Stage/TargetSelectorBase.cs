using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージの目標の位置の候補を出し、そのうち基準の位置に最も近いものを選ぶ。ステージごとのサブクラスをステージシーンに 1 つ置き、置いていないステージには目標がない。
    /// </summary>
    public abstract class TargetSelectorBase : MonoBehaviour
    {
        private readonly List<Vector3> _candidates = new();

        /// <summary> クリアに数える破壊対象。StageData の ID の絞り込みを済ませたもの </summary>
        protected IReadOnlyList<IDestructionTarget> ClearTargets { get; private set; } = System.Array.Empty<IDestructionTarget>();

        /// <summary>
        /// 候補を出すのに使う物を受け取る。ステージシーンを読み込み、破壊対象を集めたあとに呼ぶ。
        /// </summary>
        /// <param name="clearTargets">クリアに数える破壊対象</param>
        public void Initialize(IReadOnlyList<IDestructionTarget> clearTargets)
        {
            ClearTargets = clearTargets;
            OnInitialize();
        }

        /// <summary>
        /// 候補のうち、基準の位置からの直線距離が最も近いものを返す。
        /// </summary>
        /// <param name="origin">基準の位置</param>
        /// <param name="position">最も近い候補の位置</param>
        /// <returns>候補が 1 つ以上あれば true</returns>
        public bool TryGetNearest(Vector3 origin, out Vector3 position)
        {
            _candidates.Clear();
            CollectCandidates(_candidates);

            position = default;
            var nearestSqr = float.PositiveInfinity;
            foreach (var candidate in _candidates)
            {
                var sqr = (candidate - origin).sqrMagnitude;
                if (sqr >= nearestSqr) continue;

                nearestSqr = sqr;
                position = candidate;
            }

            return _candidates.Count > 0;
        }

        /// <summary>
        /// Initialize の最後に呼ぶ。候補の元になる物を集める。
        /// </summary>
        protected virtual void OnInitialize()
        {
        }

        /// <summary>
        /// 今の目標の位置の候補を書き足す。毎フレーム呼ぶ。
        /// </summary>
        /// <param name="candidates">書き足す先。空の状態で渡す</param>
        protected abstract void CollectCandidates(List<Vector3> candidates);
    }
}
