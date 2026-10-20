using System.Collections.Generic;
using Kizami.BlackBoard;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// プレイヤーが F キーで飛び移れる目印。
    /// 塔の帯の目印は、帯の表面に着地して周囲のボクセルを壊す。倒れた塔の根元の目印は、塔が倒れたあとに有効になり、ボクセルは壊さない。
    /// </summary>
    public sealed class FlightAnchor : MonoBehaviour
    {
        /// <summary> 有効な目印の一覧 </summary>
        private static readonly List<FlightAnchor> _anchors = new();

        [SerializeField]
        [Tooltip("塔。帯の目印は、この塔の表面へ位置を合わせる。倒れた塔の根元の目印は、この塔が倒れたあとに有効になる")]
        private TowerTopple _tower;

        [SerializeField]
        [Tooltip("true なら倒れた塔の根元へ飛ぶ目印。false なら塔の帯の表面へ飛ぶ目印")]
        private bool _isFallenTowerRoot;

        [SerializeField, Min(0f)]
        [Tooltip("着地の周囲のボクセルを壊す球の半径（m）。帯の目印でだけ使う")]
        private float _carveRadius = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("帯の目印を、塔の表面から外向きに離す距離（m）")]
        private float _surfaceOffset = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("倒れた塔の根元の目印を、根元から上へ離す距離（m）")]
        private float _rootLift = 3f;

        /// <summary> 有効な目印の一覧 </summary>
        public static IReadOnlyList<FlightAnchor> Anchors => _anchors;

        /// <summary> 今、飛び移れるか </summary>
        public bool IsAvailable => !_isFallenTowerRoot || (_tower != null && _tower.HasToppled);

        private void OnEnable()
        {
            _anchors.Add(this);
        }

        private void OnDisable()
        {
            _anchors.Remove(this);
        }

        private void Start()
        {
            if (!_isFallenTowerRoot) SnapToTowerSurface();
        }

        /// <summary>
        /// 飛び先を返す。倒れた塔の根元の目印は、倒れた塔の根元の位置を求められないとき false を返す。
        /// </summary>
        public bool TryGetTarget(out FlightTarget target)
        {
            if (_isFallenTowerRoot)
            {
                var hasRoot = _tower != null && _tower.TryGetUpperBase(out var root);
                target = hasRoot ? new FlightTarget(root + Vector3.up * _rootLift, false, 0f) : default;
                return hasRoot;
            }

            target = new FlightTarget(transform.position, true, _carveRadius);
            return true;
        }

        /// <summary>
        /// この目印の高さで、塔の中心へ向けて外からレイを飛ばし、当たった表面から外向きに離した位置へ動かす。当たらなければ動かさない。
        /// </summary>
        private void SnapToTowerSurface()
        {
            if (_tower == null) return;

            Physics.SyncTransforms();
            var center = _tower.transform.position;
            var flat = new Vector3(transform.position.x - center.x, 0f, transform.position.z - center.z);
            if (flat.sqrMagnitude < 1e-4f) return;

            var outward = flat.normalized;
            var origin = new Vector3(transform.position.x, transform.position.y, transform.position.z);
            if (!Physics.Raycast(origin, -outward, out var hit, flat.magnitude, ~0, QueryTriggerInteraction.Ignore)) return;

            transform.position = hit.point + outward * _surfaceOffset;
        }
    }
}
