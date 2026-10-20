using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 到達のステージの目標を選ぶ。倒れる前は天照塔の壊せるボクセルの帯の中心、倒れて止まったあとは塔の先端を候補にする。倒れている間は候補がない。ステージシーンへ置く。
    /// </summary>
    public sealed class TowerTargetSelector : TargetSelectorBase
    {
        [SerializeField]
        [Tooltip("目標にする塔。未設定ならステージシーンから探す")]
        private TowerTopple _tower;

        protected override void OnInitialize()
        {
            if (_tower == null) _tower = FindFirstObjectByType<TowerTopple>();
        }

        protected override void CollectCandidates(List<Vector3> candidates)
        {
            if (_tower == null) return;

            var body = _tower.SettledBody;
            if (body != null)
            {
                candidates.Add(body.position + body.transform.up * _tower.AxisLength);
                return;
            }

            if (!_tower.HasToppled && _tower.Building != null) candidates.Add(_tower.Building.InitialCenter);
        }
    }
}
