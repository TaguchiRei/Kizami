using System.Collections.Generic;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 切断で生まれたかけらを管理する。
    /// 切断の結果を受け取ると、切られた元の対象を管理から外し、表と裏のかけらを登録する。
    /// かけらがプールに回収されたとき（CuttableObject.ReuseAction）も管理から外す。
    /// </summary>
    public sealed class FragmentOrbAdapter : InitializableMonoBehaviour
    {
        /// <summary> 管理中のかけら </summary>
        private readonly HashSet<CuttableObject> _fragments = new();

        /// <summary> ReuseAction に回収時の処理を登録済みのかけら </summary>
        private readonly HashSet<CuttableObject> _hookedFragments = new();

        /// <summary> 管理中のかけらの数 </summary>
        public int FragmentCount => _fragments.Count;

        /// <summary>
        /// 切断の結果を受け取り、管理中のかけらを更新する。
        /// 元の対象を先にすべて外してから表と裏を登録する。
        /// プールが 1 回の切断の中で一周すると、元の対象が同じ切断の別の組の表や裏として使い回される為。
        /// </summary>
        /// <param name="results">MultiCutBlade.ExecuteCut の結果</param>
        public void ReceiveCutResults(MultiCutResult[] results)
        {
            if (!Initialized || results == null) return;

            foreach (var result in results)
            {
                _fragments.Remove(result.Original);
            }

            foreach (var result in results)
            {
                Track(result.Front);
                Track(result.Back);
            }
        }

        /// <summary>
        /// かけらを管理に加える。初めて見るかけらには、回収されたときに管理から外す処理を登録する。
        /// </summary>
        private void Track(CuttableObject fragment)
        {
            if (fragment == null || !fragment.gameObject.activeSelf) return;

            if (_hookedFragments.Add(fragment))
            {
                fragment.ReuseAction += () => _fragments.Remove(fragment);
            }

            _fragments.Add(fragment);
        }
    }
}
