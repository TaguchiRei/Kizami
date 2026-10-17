using System;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスの脚1本の設定。腿と脛の間や脛の先に別の部位が挟まっていてもよく、腿・脛・足先の3つの Transform の位置から骨の長さと休みの向きを読む。
    /// </summary>
    [Serializable]
    public sealed class BossLeg
    {
        [SerializeField]
        [Tooltip("腿の部位。基準点を腰に置く")]
        private Transform _upper;

        [SerializeField]
        [Tooltip("脛の部位。腿の子孫に置き、基準点を膝に置く")]
        private Transform _lower;

        [SerializeField]
        [Tooltip("足先の部位。脛の子孫に置き、基準点を地面に着く所に置く")]
        private Transform _foot;

        [SerializeField, Min(0)]
        [Tooltip("足を運ぶ組の番号。同じ番号の脚は一緒に運び、違う番号の組とは交互に運ぶ")]
        private int _pair;

        /// <summary> 腿の部位 </summary>
        public Transform Upper => _upper;

        /// <summary> 脛の部位 </summary>
        public Transform Lower => _lower;

        /// <summary> 足先の部位 </summary>
        public Transform Foot => _foot;

        /// <summary> 足を運ぶ組の番号 </summary>
        public int Pair => _pair;
    }
}
