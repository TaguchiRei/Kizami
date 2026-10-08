using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 視線の先のかけらをつかんでカメラの前に持ち、視線の先へ投げる Adapter。
    /// 持っているかけらはランチャーに 1 つだけ装填でき、装填したかけらは重力なしでまっすぐ撃ち出す。
    /// 投げたかけらと撃ったかけらは、何かにぶつかるか、寿命が来るとプールへ返す。飛ばした瞬間に重なっている相手にも当たったものとする。オーブにもチャージにもならない。
    /// 装甲のパネルにぶつかったら、粉砕タイプとしてそのパネルだけを一撃で壊す。
    /// </summary>
    /// <remarks>
    /// かけらのプールは空きがなくなると古いかけらを使い回すので、運んでいるかけらと飛んでいるかけらが回収されたら手放し、物理の設定を戻す。
    /// 飛んでいるかけらは連続の衝突判定にする。薄い装甲のパネルを 1 ステップで越えると奥の物にも同時に触れ、奥の物への接触が先に届くとパネルを壊さずにプールへ返る為。
    /// </remarks>
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    public sealed class FragmentThrowAdapter : InitializableMonoBehaviour
    {
        /// <summary> 実行順。CinemachineBrain（既定の 0）より後 </summary>
        private const int EXECUTION_ORDER = 100;

        /// <summary> つかむ範囲から一度に集めるコライダーの最大数。かけら 1 つが球のコライダーを 10 個持つので、かけら約 25 個ぶん </summary>
        private const int MAX_HIT_COUNT = 256;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[MAX_HIT_COUNT];

        /// <summary> つかむ候補と、視線の中心からの角度（度） </summary>
        private readonly List<(float Angle, CuttableObject Fragment)> _candidates = new();

        /// <summary> 投げたかけらと撃ったかけらと、飛ばした時刻（Time.time）と、飛ばす前の衝突判定の方式 </summary>
        private readonly Dictionary<CuttableObject, (float LaunchedTime, CollisionDetectionMode CollisionMode)>
            _flyingFragments = new();

        /// <summary> 回収時と接触時の処理を登録済みのかけら </summary>
        private readonly HashSet<CuttableObject> _hookedFragments = new();

        /// <summary> 寿命が来た飛んでいるかけらを集める作業用の一覧 </summary>
        private readonly List<CuttableObject> _expiredFragments = new();

        [SerializeField]
        [Tooltip("かけらのプール。飛ばし終わったかけらを返す先")]
        private MeshCutObjectPool _fragmentPool;

        [SerializeField]
        [Tooltip("つかむかけらを探すレイヤー")]
        private LayerMask _fragmentLayers;

        [SerializeField]
        [Tooltip("飛ばす先を決めるレイが当たるレイヤー。プレイヤーとかけらのレイヤーは外す")]
        private LayerMask _aimLayers = ~0;

        [SerializeField, Min(0f)]
        [Tooltip("飛ばす先を決めるレイの長さ（m）。何にも当たらなければ、視線の先のこの距離の点へ飛ばす")]
        private float _aimDistance = 100f;

        [SerializeField]
        [Tooltip("持っているかけらの中心を置く位置（カメラのローカル座標、m）")]
        private Vector3 _holdOffset = new(0.4f, -0.3f, 1.2f);

        [SerializeField]
        [Tooltip("装填したかけらの中心を置く位置（カメラのローカル座標、m）")]
        private Vector3 _loadedOffset = new(0.7f, -0.6f, 1.2f);

        [SerializeField, Min(0f)]
        [Tooltip("何にもぶつからない飛んでいるかけらを、プールへ返すまでの時間（秒）")]
        private float _flyingLifetime = 3f;

        private Func<CuttableObject, bool> _tryTake;
        private CarriedFragment _held;
        private CarriedFragment _loaded;

        /// <summary> かけらを持っているか </summary>
        public bool IsHolding => _held.Fragment != null;

        /// <summary> ランチャーにかけらを装填しているか </summary>
        public bool IsLoaded => _loaded.Fragment != null;

        /// <summary>
        /// かけらの見た目の中心を返す。Renderer がなければ Transform の位置を返す。
        /// </summary>
        private static Vector3 GetCenter(CuttableObject fragment)
        {
            return fragment.Renderer != null ? fragment.Renderer.bounds.center : fragment.transform.position;
        }

        /// <param name="tryTake">かけらを、オーブにする管理から外す関数。外せたかけらだけをつかむ</param>
        public void Initialize(Func<CuttableObject, bool> tryTake)
        {
            _tryTake = tryTake;

            if (_fragmentPool == null)
            {
                UsefulLogger.LogError("かけらのプールが設定されていません。", this);
            }

            base.Initialize();
        }

        /// <summary>
        /// カメラから視線の向きへ球を飛ばし、当たったかけらのうち、視線の中心からの角度が最も小さいものをつかむ。
        /// オーブにする管理から外せないかけら（オーブになったもの、切り直しの途中のもの、飛んでいるもの）は、次に角度が小さいものを試す。
        /// </summary>
        /// <param name="range">球を飛ばす距離（m）</param>
        /// <param name="radius">球の半径（m）</param>
        /// <returns>つかめたら true。既に持っているときは false</returns>
        public bool TryGrab(float range, float radius)
        {
            if (!Initialized || _tryTake == null || IsHolding) return false;

            var cameraMain = Camera.main;
            if (cameraMain == null) return false;

            var cameraTransform = cameraMain.transform;
            CollectCandidates(cameraTransform.position, cameraTransform.forward, range, radius);

            foreach (var (_, fragment) in _candidates)
            {
                if (!_tryTake(fragment)) continue;

                _held = Carry(fragment, cameraTransform, _holdOffset);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 持っているかけらを、視線の先へ重力ありで投げる。
        /// </summary>
        /// <param name="speed">初速（m/s）</param>
        public void Throw(float speed)
        {
            if (!Initialized || !IsHolding) return;

            Launch(_held, speed, true);
            _held = default;
        }

        /// <summary>
        /// 持っているかけらをランチャーに装填する。装填できるのは 1 つだけ。
        /// </summary>
        /// <returns>装填できたら true。持っていないか、既に装填しているときは false</returns>
        public bool TryLoad()
        {
            if (!Initialized || !IsHolding || IsLoaded) return false;

            _loaded = _held;
            _held = default;
            return true;
        }

        /// <summary>
        /// 装填したかけらを、視線の先へ重力なしでまっすぐ撃ち出す。
        /// </summary>
        /// <param name="speed">速さ（m/s）</param>
        public void Fire(float speed)
        {
            if (!Initialized || !IsLoaded) return;

            Launch(_loaded, speed, false);
            _loaded = default;
        }

        private void Update()
        {
            if (!Initialized) return;

            ReleaseExpiredFragments();
        }

        private void LateUpdate()
        {
            if (!Initialized || (!IsHolding && !IsLoaded)) return;

            var cameraMain = Camera.main;
            if (cameraMain == null) return;

            var cameraTransform = cameraMain.transform;
            if (IsHolding) FollowCamera(_held, cameraTransform, _holdOffset);
            if (IsLoaded) FollowCamera(_loaded, cameraTransform, _loadedOffset);
        }

        /// <summary>
        /// 球が当たったアクティブなかけらを、視線の中心からの角度が小さい順に集める。
        /// </summary>
        private void CollectCandidates(Vector3 origin, Vector3 forward, float range, float radius)
        {
            _candidates.Clear();

            var hitCount = Physics.SphereCastNonAlloc(origin, radius, forward, _hitBuffer, range, _fragmentLayers,
                QueryTriggerInteraction.Ignore);

            if (hitCount == _hitBuffer.Length)
            {
                UsefulLogger.LogWarning($"つかむ範囲のコライダーが上限（{MAX_HIT_COUNT}）に達しました。", this);
            }

            for (var i = 0; i < hitCount; i++)
            {
                if (!_hitBuffer[i].collider.TryGetComponent(out CuttableObject fragment)) continue;
                if (!fragment.gameObject.activeSelf || ContainsCandidate(fragment)) continue;

                _candidates.Add((Vector3.Angle(forward, GetCenter(fragment) - origin), fragment));
            }

            _candidates.Sort((a, b) => a.Angle.CompareTo(b.Angle));
        }

        private bool ContainsCandidate(CuttableObject fragment)
        {
            foreach (var (_, candidate) in _candidates)
            {
                if (candidate == fragment) return true;
            }

            return false;
        }

        /// <summary>
        /// かけらの物理を止め、当たり判定と切断の対象から外して、カメラの前の指定した位置に置く。
        /// </summary>
        private CarriedFragment Carry(CuttableObject fragment, Transform cameraTransform, Vector3 offset)
        {
            Hook(fragment);

            var rigidbody = fragment.Rig;
            var carried = new CarriedFragment(fragment, rigidbody.interpolation, rigidbody.collisionDetectionMode,
                Quaternion.Inverse(cameraTransform.rotation) * fragment.transform.rotation,
                fragment.transform.InverseTransformPoint(GetCenter(fragment)));

            rigidbody.interpolation = RigidbodyInterpolation.None;
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = false;
            fragment.DisableCutting();

            FollowCamera(carried, cameraTransform, offset);
            return carried;
        }

        /// <summary>
        /// 運んでいるかけらを、運び始めたときのカメラから見た向きのまま、見た目の中心が指定した位置に来るように置く。
        /// </summary>
        private void FollowCamera(CarriedFragment carried, Transform cameraTransform, Vector3 offset)
        {
            var fragmentTransform = carried.Fragment.transform;
            fragmentTransform.rotation = cameraTransform.rotation * carried.Rotation;

            var centerOffset = fragmentTransform.TransformPoint(carried.LocalCenter) - fragmentTransform.position;
            fragmentTransform.position = cameraTransform.TransformPoint(offset) - centerOffset;
        }

        /// <summary>
        /// 運んでいるかけらの物理を戻し、中心から視線の先の狙った点へ向けて飛ばす。
        /// </summary>
        private void Launch(CarriedFragment carried, float speed, bool useGravity)
        {
            var fragment = carried.Fragment;
            var rigidbody = fragment.Rig;
            rigidbody.isKinematic = false;
            rigidbody.detectCollisions = true;
            rigidbody.interpolation = carried.Interpolation;
            rigidbody.useGravity = useGravity;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var cameraMain = Camera.main;
            var direction = cameraMain != null
                ? (GetAimPoint(cameraMain.transform) - GetCenter(fragment)).normalized
                : fragment.transform.forward;
            rigidbody.linearVelocity = direction * speed;
            rigidbody.angularVelocity = Vector3.zero;

            _flyingFragments[fragment] = (Time.time, carried.CollisionMode);
        }

        /// <summary>
        /// 視線の先でレイが当たった点を返す。何にも当たらなければ、視線の先の _aimDistance の点を返す。
        /// </summary>
        private Vector3 GetAimPoint(Transform cameraTransform)
        {
            var origin = cameraTransform.position;
            var forward = cameraTransform.forward;

            return Physics.Raycast(origin, forward, out var hit, _aimDistance, _aimLayers, QueryTriggerInteraction.Ignore)
                ? hit.point
                : origin + forward * _aimDistance;
        }

        /// <summary>
        /// 初めて扱うかけらに、回収されたときに手放す処理と、ぶつかったときにプールへ返す処理を登録する。
        /// </summary>
        private void Hook(CuttableObject fragment)
        {
            if (!_hookedFragments.Add(fragment)) return;

            fragment.ReuseAction += () => OnReused(fragment);

            if (fragment.TryGetComponent(out FragmentContactReporter reporter))
            {
                reporter.Touched += OnFragmentTouched;
            }
            else
            {
                UsefulLogger.LogWarning("かけらに FragmentContactReporter がない為、飛ばしてもぶつかったときに消えません。", fragment);
            }
        }

        /// <summary>
        /// 飛んでいるかけらがぶつかったときにプールへ返す。ぶつかった相手が装甲のパネルなら、粉砕タイプとしてそのパネルを一撃で壊す。
        /// </summary>
        private void OnFragmentTouched(CuttableObject fragment, Collider other)
        {
            if (!_flyingFragments.ContainsKey(fragment)) return;

            if (other.TryGetComponent(out ArmorPanel armorPanel)) armorPanel.Shatter();

            Release(fragment);
        }

        /// <summary>
        /// 寿命が来た飛んでいるかけらをプールへ返す。
        /// </summary>
        private void ReleaseExpiredFragments()
        {
            _expiredFragments.Clear();

            foreach (var (fragment, flying) in _flyingFragments)
            {
                if (Time.time - flying.LaunchedTime >= _flyingLifetime) _expiredFragments.Add(fragment);
            }

            foreach (var fragment in _expiredFragments)
            {
                Release(fragment);
            }
        }

        /// <summary>
        /// かけらをプールへ返す。手放す処理と物理の設定を戻す処理は、返すときに呼ばれる OnReused が行う。
        /// </summary>
        private void Release(CuttableObject fragment)
        {
            if (_fragmentPool != null)
            {
                _fragmentPool.ReleaseObject(fragment);
            }
            else
            {
                OnReused(fragment);
                fragment.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// かけらが回収されたときに、運んでいるかけらなら手放し、飛んでいるかけらなら一覧から外して、物理の設定をプレハブの状態へ戻す。
        /// </summary>
        private void OnReused(CuttableObject fragment)
        {
            if (_flyingFragments.Remove(fragment, out var flying))
            {
                fragment.Rig.collisionDetectionMode = flying.CollisionMode;
            }
            else if (fragment == _held.Fragment)
            {
                fragment.Rig.interpolation = _held.Interpolation;
                _held = default;
            }
            else if (fragment == _loaded.Fragment)
            {
                fragment.Rig.interpolation = _loaded.Interpolation;
                _loaded = default;
            }
            else
            {
                return;
            }

            var rigidbody = fragment.Rig;
            rigidbody.isKinematic = false;
            rigidbody.detectCollisions = true;
            rigidbody.useGravity = true;
        }

        /// <summary>
        /// カメラの前に運んでいるかけらと、運び始めたときの状態。
        /// </summary>
        private readonly struct CarriedFragment
        {
            /// <summary> 運んでいるかけら。運んでいなければ null </summary>
            public readonly CuttableObject Fragment;

            /// <summary> 運び始めたときの補間の設定。飛ばすときと回収されたときに戻す </summary>
            public readonly RigidbodyInterpolation Interpolation;

            /// <summary> 運び始めたときの衝突判定の方式。飛んだあと回収されたときに戻す </summary>
            public readonly CollisionDetectionMode CollisionMode;

            /// <summary> カメラから見た、かけらの向き </summary>
            public readonly Quaternion Rotation;

            /// <summary> かけらのローカル座標での、見た目の中心 </summary>
            public readonly Vector3 LocalCenter;

            public CarriedFragment(CuttableObject fragment, RigidbodyInterpolation interpolation,
                CollisionDetectionMode collisionMode, Quaternion rotation, Vector3 localCenter)
            {
                Fragment = fragment;
                Interpolation = interpolation;
                CollisionMode = collisionMode;
                Rotation = rotation;
                LocalCenter = localCenter;
            }
        }
    }
}
