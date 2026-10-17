using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using UsefulToolkit.MeshCut;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 視線の先のかけらをつかんでカメラの前に持ち、視線の先へ投げる Adapter。
    /// 持っているかけらはランチャーに1つだけ装填でき、装填したかけらは重力なしでまっすぐ撃ち出す。
    /// 投げたかけらと撃ったかけらは、何かにぶつかるか、寿命が来るとプールへ返す。飛ばした瞬間に重なっている相手にも当たったものとする。オーブにもチャージにもならない。
    /// 装甲のパネルにぶつかったら、粉砕タイプとしてそのパネルだけを一撃で壊す。爆発させて飛ばしたかけらは、ぶつかった所で爆発する。
    /// かけらと同じ操作で、Brilliant Skill の言葉（BrilliantWord）もつかんで投げられる。言葉は全体を 1 つの的にして、丸ごとつかむ。
    /// 言葉はかけらの装填はできず、重力を無視してまっすぐ、かけらの投擲の _wordThrowSpeedScale 倍の速さで飛ぶ。
    /// </summary>
    /// <remarks>
    /// かけらのプールは空きがなくなると古いかけらを使い回すので、運んでいるかけらと飛んでいるかけらが回収されたら手放し、物理の設定を戻す。
    /// 飛んでいるかけらは連続の衝突判定にする。薄い装甲のパネルを1ステップで越えると奥の物にも同時に触れ、奥の物への接触が先に届くとパネルを壊さずにプールへ返る為。
    /// </remarks>
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    public sealed class FragmentThrowAdapter : InitializableMonoBehaviour
    {
        /// <summary> 実行順。CinemachineBrain（既定の0）より後 </summary>
        private const int EXECUTION_ORDER = 100;

        /// <summary> 言葉を狙う視線の円柱を、周りで調べるレイの数（中心のレイを除く） </summary>
        private const int AIM_RAY_COUNT = 8;

        /// <summary> つかむ範囲から一度に集めるコライダーの最大数。かけら1つが球のコライダーを10個持つので、かけら約25個ぶん </summary>
        private const int MAX_HIT_COUNT = 256;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[MAX_HIT_COUNT];

        /// <summary> つかむ候補と、視線の中心からの角度（度） </summary>
        private readonly List<(float Angle, CuttableObject Fragment)> _candidates = new();

        /// <summary> 投げたかけらと撃ったかけらと、飛ばした時刻（Time.time）と、飛ばす前の衝突判定の方式と、当たった所で爆発するか </summary>
        private readonly Dictionary<CuttableObject,
            (float LaunchedTime, CollisionDetectionMode CollisionMode, bool IsExplosive)> _flyingFragments = new();

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

        [SerializeField, Min(0.01f)]
        [Tooltip("爆発するかけらが当たったときの爆発の半径（m）")]
        private float _explosionRadius = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("言葉をつかめる距離（m）。言葉は遠くに大きく出る為、かけらより長い")]
        private float _wordGrabDistance = 80f;

        [SerializeField, Min(0f)]
        [Tooltip("言葉を狙う視線の太さ（半径, m）。視線を中心にこの半径の円柱が言葉の箱に当たればつかめる")]
        private float _wordGrabAimRadius = 1f;

        [SerializeField]
        [Tooltip("持っている言葉の中心を置く位置（カメラのローカル座標、m）")]
        private Vector3 _wordHoldOffset = new(0.5f, -0.3f, 1.6f);

        [SerializeField, Min(0.01f)]
        [Tooltip("持っている間の言葉の長さ（m）。箱の最も長い辺がこの長さになる大きさにする。投げた言葉もこの大きさで飛ぶ")]
        private float _wordHoldLength = 1.2f;

        [SerializeField, Min(0.1f)]
        [Tooltip("言葉を投げる速さの、かけらの投擲の速さに対する倍率")]
        private float _wordThrowSpeedScale = 2f;

        private Func<CuttableObject, bool> _tryTake;
        private Action<Vector3, float> _onExplode;
        private CarriedFragment _held;
        private CarriedFragment _loaded;
        private BrilliantSkillTextAdapter _wordSource;
        private BrilliantWord _heldWord;

        /// <summary> かけらか言葉を持っているか </summary>
        public bool IsHolding => _held.Fragment != null || _heldWord != null;

        /// <summary> ランチャーにかけらを装填しているか </summary>
        public bool IsLoaded => _loaded.Fragment != null;

        /// <summary>
        /// かけらの見た目の中心を返す。Renderer がなければ Transform の位置を返す。
        /// </summary>
        private static Vector3 GetCenter(CuttableObject fragment)
        {
            return fragment.Renderer != null ? fragment.Renderer.bounds.center : fragment.transform.position;
        }

        /// <summary>
        /// つかめる言葉の取得元を設定する。設定するまで、言葉はつかめない。
        /// </summary>
        /// <param name="wordSource">言葉を出している Adapter</param>
        public void SetWordSource(BrilliantSkillTextAdapter wordSource)
        {
            _wordSource = wordSource;
        }

        /// <param name="tryTake">かけらを、オーブにする管理から外す関数。外せたかけらだけをつかむ</param>
        /// <param name="onExplode">爆発するかけらが当たったときに呼ぶ関数。引数は爆発の中心と半径（m）</param>
        public void Initialize(Func<CuttableObject, bool> tryTake, Action<Vector3, float> onExplode)
        {
            _tryTake = tryTake;
            _onExplode = onExplode;

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
        /// 視線の先にある言葉を、全体を 1 つの的として丸ごとつかむ。視線を中心にした太さ _wordGrabAimRadius の円柱を、
        /// カメラから _wordGrabDistance まで飛ばし、言葉の箱の当たり判定に最も近く当たった言葉をつかむ。投げられて飛んでいる言葉はつかめない。
        /// </summary>
        /// <returns>つかめたら true。既に持っているときと、つかめる言葉がないときは false</returns>
        public bool TryGrabWord()
        {
            if (!Initialized || _wordSource == null || IsHolding) return false;

            var cameraMain = Camera.main;
            if (cameraMain == null) return false;

            var cameraTransform = cameraMain.transform;
            BrilliantWord nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var word in _wordSource.Words)
            {
                if (word == null || word.IsThrown || word.IsCarried) continue;
                if (!TryHitAim(word.Box, cameraTransform, out var distance) || distance >= nearestDistance) continue;

                nearest = word;
                nearestDistance = distance;
            }

            if (nearest == null) return false;

            nearest.BeginCarry(_wordHoldLength);
            nearest.FollowCamera(cameraTransform, _wordHoldOffset);
            _heldWord = nearest;
            return true;
        }

        /// <summary>
        /// 持っているかけらか言葉を、視線の先へ投げる。かけらは重力ありで、言葉は重力なしで速く飛ぶ。言葉は爆発しない。
        /// </summary>
        /// <param name="speed">かけらの初速（m/s）。言葉は _wordThrowSpeedScale 倍で飛ぶ</param>
        /// <param name="isExplosive">かけらを当たった所で爆発させるか</param>
        /// <returns>投げたら true。持っていないときは false</returns>
        public bool TryThrow(float speed, bool isExplosive)
        {
            if (!Initialized || !IsHolding) return false;

            if (_heldWord != null)
            {
                ThrowWord(speed * _wordThrowSpeedScale);
                return true;
            }

            Launch(_held, speed, true, isExplosive);
            _held = default;
            return true;
        }

        /// <summary>
        /// 持っているかけらをランチャーに装填する。装填できるのは1つだけ。
        /// </summary>
        /// <returns>装填できたら true。持っていないか、既に装填しているときは false</returns>
        public bool TryLoad()
        {
            if (!Initialized || _held.Fragment == null || IsLoaded) return false;

            _loaded = _held;
            _held = default;
            return true;
        }

        /// <summary>
        /// 装填したかけらを、視線の先へ重力なしでまっすぐ撃ち出す。
        /// </summary>
        /// <param name="speed">速さ（m/s）</param>
        /// <param name="isExplosive">当たった所で爆発させるか</param>
        /// <returns>撃ったら true。装填していないときは false</returns>
        public bool TryFire(float speed, bool isExplosive)
        {
            if (!Initialized || !IsLoaded) return false;

            Launch(_loaded, speed, false, isExplosive);
            _loaded = default;
            return true;
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
            if (_held.Fragment != null) FollowCamera(_held, cameraTransform, _holdOffset);
            if (IsLoaded) FollowCamera(_loaded, cameraTransform, _loadedOffset);
            if (_heldWord != null) _heldWord.FollowCamera(cameraTransform, _wordHoldOffset);
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
        private void Launch(CarriedFragment carried, float speed, bool useGravity, bool isExplosive)
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

            _flyingFragments[fragment] = (Time.time, carried.CollisionMode, isExplosive);
        }

        /// <summary>
        /// 持っている言葉を、中心から視線の先の狙った点へ向けて飛ばす。
        /// </summary>
        private void ThrowWord(float speed)
        {
            var word = _heldWord;
            _heldWord = null;

            var cameraMain = Camera.main;
            var direction = cameraMain != null
                ? (GetAimPoint(cameraMain.transform) - word.Center).normalized
                : word.transform.forward;
            word.Launch(direction, speed, _flyingLifetime);
        }

        /// <summary>
        /// カメラの視線を中心にした円柱（太さ _wordGrabAimRadius、長さ _wordGrabDistance）が、箱に当たるか。
        /// 視線と、視線に垂直な 8 方向へ半径だけずらした平行なレイの、計 9 本で調べ、最も近い当たりまでの距離を返す。
        /// </summary>
        private bool TryHitAim(BoxCollider box, Transform cameraTransform, out float distance)
        {
            distance = float.MaxValue;
            var forward = cameraTransform.forward;
            var found = false;

            for (var i = -1; i < AIM_RAY_COUNT; i++)
            {
                var offset = Vector3.zero;
                if (i >= 0)
                {
                    var angle = i * Mathf.PI * 2f / AIM_RAY_COUNT;
                    offset = (cameraTransform.right * Mathf.Cos(angle) + cameraTransform.up * Mathf.Sin(angle)) *
                             _wordGrabAimRadius;
                }

                var ray = new Ray(cameraTransform.position + offset, forward);
                if (!box.Raycast(ray, out var hit, _wordGrabDistance) || hit.distance >= distance) continue;

                distance = hit.distance;
                found = true;
            }

            return found;
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
        /// 爆発するかけらなら、かけらの中心で爆発させる。
        /// </summary>
        private void OnFragmentTouched(CuttableObject fragment, Collider other)
        {
            if (!_flyingFragments.TryGetValue(fragment, out var flying)) return;

            if (other.TryGetComponent(out ArmorPanel armorPanel)) armorPanel.ApplyDamage(DamageType.Shatter);
            if (flying.IsExplosive) _onExplode?.Invoke(GetCenter(fragment), _explosionRadius);

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
