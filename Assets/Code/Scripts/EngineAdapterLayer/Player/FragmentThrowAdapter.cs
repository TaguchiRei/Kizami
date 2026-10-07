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
    /// 持っている間のかけらは物理を止め、当たり判定と切断の対象から外す。
    /// 投げたかけらは、猶予時間を過ぎてから何かにぶつかるか、寿命が来るとプールへ返す。オーブにもチャージにもならない。
    /// </summary>
    /// <remarks>
    /// かけらのプールは空きがなくなると古いかけらを使い回すので、持っているかけらと投げたかけらが回収されたら手放し、物理の設定を戻す。
    /// 投げたかけらの時間は Time.time で数え、スローモード中は一緒に遅くなる。
    /// </remarks>
    public sealed class FragmentThrowAdapter : InitializableMonoBehaviour
    {
        /// <summary> つかむ範囲から一度に集めるコライダーの最大数 </summary>
        private const int MAX_HIT_COUNT = 64;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[MAX_HIT_COUNT];

        /// <summary> つかむ候補と、視線の中心からの角度（度） </summary>
        private readonly List<(float Angle, CuttableObject Fragment)> _candidates = new();

        /// <summary> 投げたかけらと、投げた時刻（Time.time） </summary>
        private readonly Dictionary<CuttableObject, float> _thrownFragments = new();

        /// <summary> 回収時と接触時の処理を登録済みのかけら </summary>
        private readonly HashSet<CuttableObject> _hookedFragments = new();

        /// <summary> 寿命が来た投げたかけらを集める作業用の一覧 </summary>
        private readonly List<CuttableObject> _expiredFragments = new();

        [SerializeField]
        [Tooltip("かけらのプール。投げ終わったかけらを返す先")]
        private MeshCutObjectPool _fragmentPool;

        [SerializeField]
        [Tooltip("つかむかけらを探すレイヤー")]
        private LayerMask _fragmentLayers;

        [SerializeField]
        [Tooltip("投げる先を決めるレイが当たるレイヤー。プレイヤーとかけらのレイヤーは外す")]
        private LayerMask _aimLayers = ~0;

        [SerializeField, Min(0f)]
        [Tooltip("投げる先を決めるレイの長さ（m）。何にも当たらなければ、視線の先のこの距離の点へ投げる")]
        private float _aimDistance = 100f;

        [SerializeField]
        [Tooltip("持っているかけらの中心を置く位置（カメラのローカル座標、m）")]
        private Vector3 _holdOffset = new(0.4f, -0.3f, 1.2f);

        [SerializeField, Min(0f)]
        [Tooltip("投げてから、ぶつかってもプールへ返さない時間（秒）")]
        private float _contactGraceTime = 0.1f;

        [SerializeField, Min(0f)]
        [Tooltip("何にもぶつからない投げたかけらを、プールへ返すまでの時間（秒）")]
        private float _thrownLifetime = 3f;

        private Func<CuttableObject, bool> _tryTake;
        private CuttableObject _heldFragment;

        /// <summary> つかんだときのかけらの補間の設定。投げるときと回収されたときに戻す </summary>
        private RigidbodyInterpolation _heldInterpolation;

        /// <summary> カメラから見た、持っているかけらの向き </summary>
        private Quaternion _heldRotation;

        /// <summary> 持っているかけらのローカル座標での、見た目の中心 </summary>
        private Vector3 _heldLocalCenter;

        /// <summary> かけらを持っているか </summary>
        public bool IsHolding => _heldFragment != null;

        /// <summary>
        /// PlayerInitializer から呼ばれる。
        /// </summary>
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
        /// オーブにする管理から外せないかけら（オーブになったもの、切り直しの途中のもの、投げたもの）は、次に角度が小さいものを試す。
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
            var origin = cameraTransform.position;
            var forward = cameraTransform.forward;

            CollectCandidates(origin, forward, range, radius);

            foreach (var (_, fragment) in _candidates)
            {
                if (!_tryTake(fragment)) continue;

                Hold(fragment, cameraTransform);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 持っているかけらを、視線の先へ重力ありで投げる。持っていなければ何もしない。
        /// </summary>
        /// <param name="speed">初速（m/s）</param>
        public void Throw(float speed)
        {
            if (!Initialized || !IsHolding) return;

            var cameraMain = Camera.main;
            if (cameraMain == null) return;

            var fragment = _heldFragment;
            _heldFragment = null;

            var rigidbody = fragment.Rig;
            rigidbody.isKinematic = false;
            rigidbody.detectCollisions = true;
            rigidbody.interpolation = _heldInterpolation;
            rigidbody.useGravity = true;

            var start = fragment.Renderer != null ? fragment.Renderer.bounds.center : fragment.transform.position;
            rigidbody.linearVelocity = (GetAimPoint(cameraMain.transform) - start).normalized * speed;
            rigidbody.angularVelocity = Vector3.zero;

            _thrownFragments[fragment] = Time.time;
        }

        private void Update()
        {
            if (!Initialized) return;

            ReleaseExpiredFragments();
        }

        private void LateUpdate()
        {
            if (!Initialized || !IsHolding) return;

            var cameraMain = Camera.main;
            if (cameraMain == null) return;

            FollowCamera(_heldFragment.transform, cameraMain.transform);
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

                var center = fragment.Renderer != null ? fragment.Renderer.bounds.center : fragment.transform.position;
                _candidates.Add((Vector3.Angle(forward, center - origin), fragment));
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
        /// かけらの物理を止め、当たり判定と切断の対象から外して、カメラの前に置く。
        /// </summary>
        private void Hold(CuttableObject fragment, Transform cameraTransform)
        {
            Hook(fragment);

            var rigidbody = fragment.Rig;
            _heldInterpolation = rigidbody.interpolation;
            rigidbody.interpolation = RigidbodyInterpolation.None;
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = false;
            fragment.DisableCutting();

            var center = fragment.Renderer != null ? fragment.Renderer.bounds.center : fragment.transform.position;
            _heldLocalCenter = fragment.transform.InverseTransformPoint(center);
            _heldRotation = Quaternion.Inverse(cameraTransform.rotation) * fragment.transform.rotation;
            _heldFragment = fragment;

            FollowCamera(fragment.transform, cameraTransform);
        }

        /// <summary>
        /// 持っているかけらを、つかんだときのカメラから見た向きのまま、見た目の中心が持つ位置に来るように置く。
        /// </summary>
        private void FollowCamera(Transform fragmentTransform, Transform cameraTransform)
        {
            fragmentTransform.rotation = cameraTransform.rotation * _heldRotation;

            var centerOffset = fragmentTransform.TransformPoint(_heldLocalCenter) - fragmentTransform.position;
            fragmentTransform.position = cameraTransform.TransformPoint(_holdOffset) - centerOffset;
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
                UsefulLogger.LogWarning("かけらに FragmentContactReporter がない為、投げてもぶつかったときに消えません。", fragment);
            }
        }

        /// <summary>
        /// 投げたかけらが、猶予時間を過ぎてからぶつかったときにプールへ返す。
        /// </summary>
        private void OnFragmentTouched(CuttableObject fragment)
        {
            if (!_thrownFragments.TryGetValue(fragment, out var thrownTime)) return;
            if (Time.time - thrownTime < _contactGraceTime) return;

            // TODO: 区間8 で、ぶつかった相手が装甲なら粉砕タイプのダメージを通す
            Release(fragment);
        }

        /// <summary>
        /// 寿命が来た投げたかけらをプールへ返す。
        /// </summary>
        private void ReleaseExpiredFragments()
        {
            _expiredFragments.Clear();

            foreach (var (fragment, thrownTime) in _thrownFragments)
            {
                if (Time.time - thrownTime >= _thrownLifetime) _expiredFragments.Add(fragment);
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
        /// かけらが回収されたときに、持っているかけらなら手放し、投げたかけらなら一覧から外して、物理の設定をプレハブの状態へ戻す。
        /// 扱っていないかけらでは何もしない。
        /// </summary>
        private void OnReused(CuttableObject fragment)
        {
            if (fragment == _heldFragment)
            {
                _heldFragment = null;
                fragment.Rig.interpolation = _heldInterpolation;
            }
            else if (!_thrownFragments.Remove(fragment))
            {
                return;
            }

            var rigidbody = fragment.Rig;
            rigidbody.isKinematic = false;
            rigidbody.detectCollisions = true;
            rigidbody.useGravity = true;
        }
    }
}
