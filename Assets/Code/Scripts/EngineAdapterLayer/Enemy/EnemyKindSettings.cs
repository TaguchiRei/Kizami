using System;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の種類 1 つの体の設定。EnemySpawnAdapter が種類ごとに 1 件持つ。
    /// </summary>
    [Serializable]
    public sealed class EnemyKindSettings
    {
        [SerializeField]
        [Tooltip("敵の種類")]
        private EnemyKind _kind;

        [SerializeField]
        [Tooltip("体のプレハブ。まとめて描画する部位のメッシュ・マテリアル・位置もここから読む")]
        private EnemyBody _bodyPrefab;

        [SerializeField, Min(0)]
        [Tooltip("体の数。近くの敵に貸す切断できる体で、初期化のときにこの数だけ作る")]
        private int _bodyCount = 32;

        /// <summary> 敵の種類 </summary>
        public EnemyKind Kind => _kind;

        /// <summary> 体のプレハブ </summary>
        public EnemyBody BodyPrefab => _bodyPrefab;

        /// <summary> 初期化のときに作る体の数 </summary>
        public int BodyCount => _bodyCount;
    }
}
