using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスの脚のミサイルを扱う。BossWeapons と同じ GameObject に付ける。
    /// 撃つときに、プレイヤーのいる地点の真下の地面を狙いとして決め、ミサイルの武装の位置から放物線で飛ばす。
    /// 着弾したとき、狙いを中心とする半径の中にプレイヤーがいればダメージを与える。飛んでいる間は、狙いに予兆のデカールを出す。
    /// 撃った所に煙を、着弾した所に爆発を出す。ミサイルの見た目に TrailRenderer があれば、撃つたびに軌跡を消してから飛ばす。
    /// </summary>
    /// <remarks>
    /// 撃った後にプレイヤーが動いても狙いは変えないので、予兆の輪から出れば避けられる。
    /// ミサイルの見た目はプレハブを最初にプールへ作って使い回し、足りなければ撃たない。
    /// </remarks>
    [RequireComponent(typeof(BossWeapons))]
    public sealed class BossMissiles : MonoBehaviour
    {
        /// <summary> 予兆のデカールを地面へ投影する奥行き（m）。段差のある地面にも映るよう厚めに取る </summary>
        private const float WARNING_DEPTH = 6f;

        [SerializeField]
        [Tooltip("狙う相手（プレイヤー）")]
        private Transform _target;

        [SerializeField]
        [Tooltip("ミサイルの見た目のプレハブ。当たり判定は使わない")]
        private GameObject _missilePrefab;

        [SerializeField]
        [Tooltip("予兆のデカールのマテリアル（URP の Decal シェーダー）")]
        private Material _warningMaterial;

        [SerializeField]
        [Tooltip("撃ったときに煙を出すパーティクル。ワールド空間でシミュレーションする。未設定なら出さない")]
        private ParticleSystem _launchSmoke;

        [SerializeField, Min(0)]
        [Tooltip("撃つたびに出す煙の粒の数")]
        private int _launchSmokeCount = 20;

        [SerializeField]
        [Tooltip("着弾したときに爆発を出すパーティクル。ワールド空間でシミュレーションする。未設定なら出さない")]
        private ParticleSystem _impactBurst;

        [SerializeField, Min(0)]
        [Tooltip("着弾するたびに出す爆発の粒の数")]
        private int _impactBurstCount = 30;

        [SerializeField]
        [Tooltip("狙いを決める地面のレイヤー")]
        private LayerMask _groundLayers = ~0;

        [SerializeField, Min(1)]
        [Tooltip("同時に飛ばせるミサイルの数")]
        private int _capacity = 8;

        [SerializeField, Min(0f)]
        [Tooltip("撃ってから次に撃てるまでの時間（秒）。壊れたミサイルの番でも過ぎる")]
        private float _cooldown = 2f;

        [SerializeField, Min(0.1f)]
        [Tooltip("撃ってから着弾するまでの時間（秒）")]
        private float _flightTime = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("放物線の頂点の、撃った所と狙いを結ぶ線からの高さ（m）")]
        private float _arcHeight = 15f;

        [SerializeField, Min(0.1f)]
        [Tooltip("着弾したときにダメージを与える半径（m）")]
        private float _radius = 3f;

        [SerializeField, Min(0)]
        [Tooltip("着弾に巻き込まれたときのダメージ量")]
        private int _damage = 10;

        [SerializeField, Min(0.1f)]
        [Tooltip("狙いを探すレイを、プレイヤーの位置からこの高さ（m）上から下へ撃つ")]
        private float _groundRayHeight = 5f;

        [SerializeField, Min(0.1f)]
        [Tooltip("狙いを探すレイの長さ（m）")]
        private float _groundRayLength = 100f;

        private BossWeapons _weapons;
        private Action<int> _applyDamage;

        /// <summary> ミサイルごとの、撃った所（ワールド座標） </summary>
        private Vector3[] _origins;

        /// <summary> ミサイルごとの、狙い（ワールド座標） </summary>
        private Vector3[] _aims;

        /// <summary> ミサイルごとの、撃ってからの割合（0〜1）。負なら使っていない </summary>
        private float[] _progress;

        private Transform[] _missiles;
        private TrailRenderer[] _trails;
        private DecalProjector[] _warnings;

        /// <summary> 次に撃てるまでの時間（秒） </summary>
        private float _cooldownRemaining;

        /// <summary>
        /// 狙う相手を target にする。ボスとプレイヤーが別のシーンにあるときに、初期化で渡す。
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        /// <summary>
        /// ダメージの関数を受け取る。
        /// </summary>
        /// <param name="applyDamage">プレイヤーにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        public void Initialize(Action<int> applyDamage)
        {
            _applyDamage = applyDamage;
        }

        /// <summary>
        /// クールタイムが過ぎていれば、次の番のミサイルを、プレイヤーのいる地点の地面へ撃つ。
        /// 番のミサイルが壊れていれば、撃たずにクールタイムだけを発生させる。
        /// </summary>
        /// <returns>撃つ番を進めたか。クールタイム中なら false</returns>
        public bool TryFire()
        {
            if (_cooldownRemaining > 0f) return false;

            var index = _weapons.TakeNext(BossWeaponKind.Missile);
            if (index < 0) return false;

            _cooldownRemaining = _cooldown;
            if (!_weapons.IsBroken(index) && _target != null) Launch(_weapons.GetPosition(index), GetAim());
            return true;
        }

        /// <summary>
        /// クールタイムを無視して、ミサイルの武装を 1 周する間に 1 回ずつ、プレイヤーのいる地点の地面へ撃つ。壊れた番では撃たない。
        /// 総攻撃で、すべての脚を撃ち切るために使う。
        /// </summary>
        /// <returns>撃った数</returns>
        public int FireAll()
        {
            if (_target == null) return 0;

            var fired = 0;
            var count = _weapons.CountOf(BossWeaponKind.Missile);
            for (var i = 0; i < count; i++)
            {
                var index = _weapons.TakeNext(BossWeaponKind.Missile);
                if (index < 0 || _weapons.IsBroken(index)) continue;

                if (Launch(_weapons.GetPosition(index), GetAim())) fired++;
            }

            return fired;
        }

        private void Awake()
        {
            _weapons = GetComponent<BossWeapons>();
            _origins = new Vector3[_capacity];
            _aims = new Vector3[_capacity];
            _progress = new float[_capacity];
            _missiles = new Transform[_capacity];
            _trails = new TrailRenderer[_capacity];
            _warnings = new DecalProjector[_capacity];

            if (_missilePrefab == null)
            {
                UsefulLogger.LogWarning("ミサイルのプレハブが設定されていない為、ミサイルは見えません。", this);
            }

            for (var i = 0; i < _capacity; i++)
            {
                _progress[i] = -1f;

                if (_missilePrefab != null)
                {
                    _missiles[i] = Instantiate(_missilePrefab, transform).transform;
                    _trails[i] = _missiles[i].GetComponentInChildren<TrailRenderer>();
                    _missiles[i].gameObject.SetActive(false);
                }

                // ボスが動いても狙いに残るよう、デカールはボスの子にせずシーンに置く
                var warning = new GameObject($"MissileWarning{i}").AddComponent<DecalProjector>();
                warning.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                warning.material = _warningMaterial;
                warning.size = new Vector3(_radius * 2f, _radius * 2f, WARNING_DEPTH);
                warning.enabled = false;
                _warnings[i] = warning;
            }
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - deltaTime);

            for (var i = 0; i < _capacity; i++)
            {
                if (_progress[i] < 0f) continue;

                _progress[i] += deltaTime / _flightTime;
                if (_progress[i] >= 1f)
                {
                    Explode(i);
                    continue;
                }

                if (_missiles[i] == null) continue;

                var position = GetArcPosition(i, _progress[i]);
                var ahead = GetArcPosition(i, Mathf.Min(1f, _progress[i] + 0.01f));
                _missiles[i].position = position;
                if ((ahead - position).sqrMagnitude > 1e-6f) _missiles[i].rotation = Quaternion.LookRotation(ahead - position);
            }
        }

        /// <summary>
        /// プレイヤーのいる地点の真下の地面。地面がなければプレイヤーの位置。
        /// </summary>
        private Vector3 GetAim()
        {
            var position = _target.position;
            var origin = position + Vector3.up * _groundRayHeight;
            return BossGroundProbe.TryGetGround(transform, origin, _groundRayLength, _groundLayers, out var ground)
                ? ground
                : position;
        }

        /// <summary>
        /// 空いているミサイルを origin から aim へ撃ち、aim に予兆の輪を出す。空きがなければ撃たない。
        /// </summary>
        /// <returns>撃ったか</returns>
        private bool Launch(Vector3 origin, Vector3 aim)
        {
            for (var i = 0; i < _capacity; i++)
            {
                if (_progress[i] >= 0f) continue;

                _origins[i] = origin;
                _aims[i] = aim;
                _progress[i] = 0f;
                if (_missiles[i] != null)
                {
                    _missiles[i].position = origin;
                    _missiles[i].gameObject.SetActive(true);
                    if (_trails[i] != null) _trails[i].Clear();
                }

                _warnings[i].transform.position = aim + Vector3.up * (WARNING_DEPTH * 0.5f);
                _warnings[i].enabled = true;
                Emit(_launchSmoke, origin, _launchSmokeCount);
                return true;
            }

            return false;
        }

        /// <summary>
        /// ミサイル i を消し、狙いを中心とする半径の中にプレイヤーがいればダメージを与える。
        /// </summary>
        private void Explode(int i)
        {
            _progress[i] = -1f;
            if (_missiles[i] != null) _missiles[i].gameObject.SetActive(false);
            _warnings[i].enabled = false;
            Emit(_impactBurst, _aims[i], _impactBurstCount);

            if (_target == null) return;
            if ((_target.position - _aims[i]).sqrMagnitude <= _radius * _radius) _applyDamage?.Invoke(_damage);
        }

        /// <summary>
        /// ミサイル i の、撃ってからの割合 t の位置。撃った所と狙いを結ぶ線に、頂点の高さの放物線を足したもの。
        /// </summary>
        private Vector3 GetArcPosition(int i, float t)
        {
            return Vector3.Lerp(_origins[i], _aims[i], t) + Vector3.up * (4f * _arcHeight * t * (1f - t));
        }

        /// <summary>
        /// パーティクル effect の粒を、位置 position に count 個出す。
        /// </summary>
        private static void Emit(ParticleSystem effect, Vector3 position, int count)
        {
            if (effect == null || count <= 0) return;

            var emitParams = new ParticleSystem.EmitParams { position = position, applyShapeToPosition = true };
            effect.Emit(emitParams, count);
        }

        private void OnDestroy()
        {
            if (_warnings == null) return;

            foreach (var warning in _warnings)
            {
                if (warning != null) Destroy(warning.gameObject);
            }
        }
    }
}
