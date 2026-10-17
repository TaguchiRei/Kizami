using UnityEngine;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージに置く小物を、通常攻撃のメッシュ切断で壊せるようにする。小物はボクセルにしない。
    /// MeshDataCache は自分の子しか自動で登録しないので、ストアができたら同じ GameObject の CuttableObject を自分で登録する。
    /// 小物は 1 回だけ切れる。CuttableObject の複数回の切断を無効にしておき、切ったかけらはもう切れない。かけらは敵のかけらと同じくオーブになる。
    /// </summary>
    [RequireComponent(typeof(CuttableObject))]
    public sealed class CuttableProp : MonoBehaviour
    {
        private CuttableObject _cuttable;

        private void Awake()
        {
            _cuttable = GetComponent<CuttableObject>();
        }

        private void Update()
        {
            var cache = MeshDataCache.Instance;
            if (cache == null || cache.Store == null) return;

            cache.Register(_cuttable);
            enabled = false;
        }
    }
}
