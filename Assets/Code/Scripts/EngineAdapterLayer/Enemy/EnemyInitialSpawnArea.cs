using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージの開始時に敵がいる範囲。EnemySpawnSystem の子に置き、複数置ける。初期化のときだけ使う。
    /// 置いた敵はグループの人数ずつグループにし、グループの中の順番で編成から種類を決める。
    /// </summary>
    public sealed class EnemyInitialSpawnArea : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        [Tooltip("この半径（m）の円の中に置く")]
        private float _radius = 5f;

        [SerializeField, Min(0)]
        [Tooltip("置く敵の数")]
        private int _count = 1;

        [SerializeField]
        [Tooltip("グループの編成。グループの先頭から順に、この並びの種類で置く。並びより後ろのメンバーは Attacker。空なら全員 Attacker")]
        private EnemyKind[] _composition = Array.Empty<EnemyKind>();

        /// <summary> 置く敵の数 </summary>
        public int Count => _count;

        /// <summary> グループの編成。グループの先頭からの種類の並び </summary>
        public IReadOnlyList<EnemyKind> Composition => _composition;

        /// <summary>
        /// グループの中の順番 memberIndex のメンバーの種類を、編成から返す。
        /// </summary>
        public EnemyKind GetKind(int memberIndex)
        {
            return memberIndex < _composition.Length ? _composition[memberIndex] : EnemyKind.Attacker;
        }

        /// <summary> 半径の円の中から、置く位置を 1 つ選ぶ </summary>
        public Vector3 GetSpawnPosition()
        {
            var offset = Random.insideUnitCircle * _radius;
            return transform.position + new Vector3(offset.x, 0f, offset.y);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
