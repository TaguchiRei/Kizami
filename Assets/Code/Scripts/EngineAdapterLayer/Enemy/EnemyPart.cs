using System;
using UnityEngine;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の体を作る部位 1 つの設定。
    /// </summary>
    [Serializable]
    public sealed class EnemyPart
    {
        [SerializeField]
        [Tooltip("部位の切断対象")]
        private CuttableObject _cuttable;

        /// <summary> 部位の切断対象 </summary>
        public CuttableObject Cuttable => _cuttable;
    }
}
