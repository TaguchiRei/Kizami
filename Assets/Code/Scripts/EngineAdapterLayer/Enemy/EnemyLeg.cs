using System;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の脚 1 本の設定。腿（体の根の直下の部位）と、その子の脛からなる。休みの姿勢では、腿の基準点（腰）から脛の先まで、まっすぐ伸びている。
    /// </summary>
    [Serializable]
    public sealed class EnemyLeg
    {
        [SerializeField]
        [Tooltip("腿の部位。体の根の直下に置き、基準点を腰に置く")]
        private Transform _upper;

        [SerializeField]
        [Tooltip("脛の部位。腿の子に置き、基準点を膝に置く")]
        private Transform _lower;

        [SerializeField, Min(0)]
        [Tooltip("足を運ぶ組の番号。同じ番号の脚は一緒に運び、違う番号の組とは交互に運ぶ（四足なら対角の 2 本ずつ）")]
        private int _pair;

        /// <summary> 腿の部位 </summary>
        public Transform Upper => _upper;

        /// <summary> 脛の部位 </summary>
        public Transform Lower => _lower;

        /// <summary> 足を運ぶ組の番号 </summary>
        public int Pair => _pair;
    }
}
