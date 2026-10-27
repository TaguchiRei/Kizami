using System.Collections.Generic;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// プレイヤーが F キーで飛び移れる目印。
    /// 塔の帯の目印は、帯の表面より少し上で塔の奥側の位置に飛び、飛ぶ前にその周囲のボクセルを壊しておく。
    /// 倒れた塔の目印は、塔の上の部分の側面（TowerFace）の子にあらかじめ置いておく。塔が倒れて止まったとき、TowerTopple が真上を向いた面の目印だけを解放し、
    /// その上側の面に着地する。ボクセルは壊さない。
    /// 塔は別のシーンにあるので、帯の目印はシーンの参照ではなく TowerTopple を探して使う。
    /// 屋上の目印は、VoxelBuilding が最上階の四隅に生成して ConfigureAsRoof で設定する。屋上の面より少し上に飛び、ボクセルは壊さない。
    /// </summary>
    public sealed class FlightAnchor : MonoBehaviour
    {
        /// <summary> 有効な目印の一覧 </summary>
        private static readonly List<FlightAnchor> _anchors = new();

        [SerializeField]
        [Tooltip("true なら倒れた塔の側面に置いた目印。解放されるまでは使えない。false なら塔の帯の表面へ飛ぶ目印")]
        private bool _isFallenTowerFace;

        [SerializeField, Min(0f)]
        [Tooltip("着地の周囲のボクセルを壊す球の半径（m）。帯の目印でだけ使う")]
        private float _carveRadius = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("帯の目印を、塔の表面から外向きに離す距離（m）")]
        private float _surfaceOffset = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("帯の目印の着地点を、目印から上へ離す距離（m）")]
        private float _bandLift = 6f;

        [SerializeField, Min(0f)]
        [Tooltip("帯の目印の着地点を、目印から塔の奥（中心の側）へ離す距離（m）")]
        private float _bandInset = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("倒れた塔の目印の着地点を、目印から上へ離す距離（m）")]
        private float _fallenTowerLift = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("倒れた塔へは、着地点よりこの高さだけ上へ上がってから下りてくる。塔の側面に当たらないようにする（m）")]
        private float _fallenTowerClearance = 15f;

        private TowerTopple _tower;
        private bool _isSnapped;
        private bool _isReleased;

        /// <summary> 建物の屋上の目印か </summary>
        private bool _isRoof;

        /// <summary> 屋上の目印の着地点を、目印から上へ離す距離（m） </summary>
        private float _roofLift;

        /// <summary> 有効な目印の一覧 </summary>
        public static IReadOnlyList<FlightAnchor> Anchors => _anchors;

        /// <summary> 今、飛び移れるか </summary>
        public bool IsAvailable => !_isFallenTowerFace || _isReleased;

        /// <summary> 飛び先がカメラよりこの高さ（m）以上高いときだけ選ぶ。塔の目印は 0 </summary>
        public float MinHeightAbove { get; private set; }

        /// <summary> 飛び先までの、カメラからの水平な距離（m）がこれ以内のときだけ選ぶ。塔の目印は無制限 </summary>
        public float MaxHorizontalDistance { get; private set; } = float.PositiveInfinity;
        private void OnEnable()
        {
            _anchors.Add(this);
        }

        private void OnDisable()
        {
            _anchors.Remove(this);
        }

        private void Update()
        {
            if (_isRoof || _isFallenTowerFace || _isSnapped || !EnsureTower()) return;

            _isSnapped = true;
            SnapToTowerSurface();
        }

        /// <summary>
        /// 倒れた塔の目印を解放する。塔が倒れて止まったあと、真上を向いた面の目印に対して TowerTopple が呼ぶ。
        /// </summary>
        public void Release()
        {
            _isReleased = true;
        }

        /// <summary>
        /// 建物の屋上の目印として設定する。この目印の位置は屋上の面の上の点。
        /// </summary>
        /// <param name="lift">着地点を、目印から上へ離す距離（m）</param>
        /// <param name="minHeightAbove">飛び先がカメラよりこの高さ（m）以上高いときだけ選ぶ</param>
        /// <param name="maxHorizontalDistance">飛び先がカメラからの水平な距離（m）でこれ以内のときだけ選ぶ</param>
        public void ConfigureAsRoof(float lift, float minHeightAbove, float maxHorizontalDistance)
        {
            _isRoof = true;
            _roofLift = lift;
            MinHeightAbove = minHeightAbove;
            MaxHorizontalDistance = maxHorizontalDistance;
        }

        /// <summary>
        /// 飛び先を返す。
        /// </summary>
        public FlightTarget GetTarget()
        {
            if (_isRoof) return new FlightTarget(transform.position + Vector3.up * _roofLift, false, 0f, 0f);

            return _isFallenTowerFace
                ? new FlightTarget(transform.position + Vector3.up * _fallenTowerLift, false, 0f,
                    _fallenTowerClearance)
                : new FlightTarget(transform.position + Vector3.up * _bandLift - GetOutward() * _bandInset, true, _carveRadius, 0f);
        }

        private bool EnsureTower()
        {
            // 塔は街のシーンにあり、ステージシーンからは Inspector で渡せない為、探す
            if (_tower == null) _tower = FindFirstObjectByType<TowerTopple>();
            return _tower != null;
        }

        /// <summary>
        /// 塔の中心から見た、この目印の水平な外向きの単位ベクトルを返す。
        /// </summary>
        private Vector3 GetOutward()
        {
            if (!EnsureTower()) return Vector3.zero;

            var center = _tower.transform.position;
            var flat = new Vector3(transform.position.x - center.x, 0f, transform.position.z - center.z);
            return flat.sqrMagnitude < 1e-4f ? Vector3.zero : flat.normalized;
        }

        /// <summary>
        /// この目印の高さで、塔の中心へ向けて外からレイを飛ばし、当たった表面から外向きに離した位置へ動かす。当たらなければ動かさない。
        /// </summary>
        private void SnapToTowerSurface()
        {
            Physics.SyncTransforms();
            var outward = GetOutward();
            if (outward == Vector3.zero) return;

            var center = _tower.transform.position;
            var distance = new Vector3(transform.position.x - center.x, 0f, transform.position.z - center.z).magnitude;
            if (!Physics.Raycast(transform.position, -outward, out var hit, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                UsefulLogger.LogWarning($"{name} の高さで塔の表面が見つからなかった為、位置をそのままにします。", this);
                return;
            }

            transform.position = hit.point + outward * _surfaceOffset;
        }
    }
}
