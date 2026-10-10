using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵のグループ 1 つの編成。メンバーの種類を隊列の順番で並べ、並びの長さをグループの人数にする。
    /// EnemySpawnPoint が参照し、スポーン位置ごとにこの編成のグループを出す。
    /// </summary>
    [CreateAssetMenu(menuName = "Kizami/Enemy/Squad Composition", fileName = "EnemySquadComposition")]
    public sealed class EnemySquadComposition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("メンバーの種類を、隊列の先頭から順に並べる。並びの長さがグループの人数（16 まで）")]
        private EnemyKind[] _members = Array.Empty<EnemyKind>();

        /// <summary> メンバーの種類の、隊列の先頭からの並び </summary>
        public IReadOnlyList<EnemyKind> Members => _members;

        private void OnValidate()
        {
            if (_members.Length > EnemyFormationSettings.MAX_GROUP_SIZE)
            {
                Array.Resize(ref _members, EnemyFormationSettings.MAX_GROUP_SIZE);
            }
        }
    }
}
