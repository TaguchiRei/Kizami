using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスの脚の Hatch が呼ぶ、降下フィニッシャーの攻撃を扱う。BossWeapons と EnemyFinisherBeams と同じ GameObject に付ける。
    /// 撃つ時点のプレイヤーの真上にユニットを置き、真下の地面に予兆のデカールを出す。警告の時間のあと、フィニッシャーのビームを真下へ撃つ。
    /// ビームのダメージとボクセルを溶かすのは EnemyFinisherBeams が行う。
    /// </summary>
    /// <remarks>
    /// 置いた後にプレイヤーが動いても位置は変えないので、予兆の輪から出れば避けられる。
    /// ユニットは見た目だけで、無敵（当たり判定を持たない）。プールに最初に作って使い回し、足りなければ撃たない。
    /// </remarks>
    [RequireComponent(typeof(BossWeapons))]
    [RequireComponent(typeof(EnemyFinisherBeams))]
    public sealed class BossFinisherStrikes : MonoBehaviour
    {
        /// <summary> 予兆のデカールを地面へ投影する奥行き（m）。段差のある地面にも映るよう厚めに取る </summary>
        private const float WARNING_DEPTH = 6f;

        [SerializeField]
        [Tooltip("狙う相手（プレイヤー）")]
        private Transform _target;

        [SerializeField]
        [Tooltip("ユニットの見た目のプレハブ。円形の空間のユニットと同じものにする。なければユニットは見えない")]
        private GameObject _unitPrefab;

        [SerializeField]
        [Tooltip("予兆のデカールのマテリアル（URP の Decal シェーダー）")]
        private Material _warningMaterial;

        [SerializeField]
        [Tooltip("狙いを決める地面のレイヤー")]
        private LayerMask _groundLayers = ~0;

        [SerializeField, Min(1)]
        [Tooltip("同時に出せるユニットの数")]
        private int _capacity = 8;

        [SerializeField, Min(0f)]
        [Tooltip("撃ってから次に撃てるまでの時間（秒）。壊れた Hatch の番でも過ぎる")]
        private float _cooldown = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("ユニットを置く、地面からの高さ（m）")]
        private float _height = 30f;

        [SerializeField, Min(0f)]
        [Tooltip("デカールで警告してから、ビームの予兆を始めるまでの時間（秒）")]
        private float _warningDuration = 3f;

        [SerializeField, Min(0.1f)]
        [Tooltip("予兆のデカールの半径（m）。ビームの危険範囲の帯の半径と合わせる")]
        private float _warningRadius = 16f;

        [SerializeField, Min(0.1f)]
        [Tooltip("狙いを探すレイを、プレイヤーの位置からこの高さ（m）上から下へ撃つ")]
        private float _groundRayHeight = 5f;

        [SerializeField, Min(0.1f)]
        [Tooltip("狙いを探すレイの長さ（m）")]
        private float _groundRayLength = 100f;

        private BossWeapons _weapons;
        private EnemyFinisherBeams.Shot[] _shots;
        private Transform[] _units;
        private DecalProjector[] _warnings;

        /// <summary> ユニットごとの、ビームを撃つ所（ワールド座標） </summary>
        private Vector3[] _pivots;

        /// <summary> ユニットごとの、ビームの予兆を始めるまでの時間（秒） </summary>
        private float[] _warningRemaining;

        /// <summary> ユニットごとの、使っているか </summary>
        private bool[] _isActive;

        /// <summary> 次に撃てるまでの時間（秒） </summary>
        private float _cooldownRemaining;

        /// <summary>
        /// 狙う相手を target にする。ボスとプレイヤーが別のシーンにあるときに、初期化で渡す。Initialize より前に呼ぶ。
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        /// <summary>
        /// ダメージの関数を受け取り、ユニットの数だけビームを作る。
        /// </summary>
        /// <param name="applyDamage">プレイヤーにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        public void Initialize(Action<int> applyDamage)
        {
            var beams = GetComponent<EnemyFinisherBeams>();
            if (!beams.Initialize(_target, applyDamage))
            {
                UsefulLogger.LogWarning("フィニッシャーのビームが使えない為、降下フィニッシャーは撃ちません。", this);
                return;
            }

            _shots = new EnemyFinisherBeams.Shot[_capacity];
            for (var i = 0; i < _capacity; i++)
            {
                _shots[i] = beams.CreateShot();
            }
        }

        /// <summary>
        /// クールタイムが過ぎていれば、次の番の Hatch でプレイヤーの真上にユニットを置く。
        /// 番の Hatch が壊れていれば、置かずにクールタイムだけを発生させる。
        /// </summary>
        /// <returns>撃つ番を進めたか。クールタイム中なら false</returns>
        public bool TryFire()
        {
            if (_cooldownRemaining > 0f) return false;

            var index = _weapons.TakeNext(BossWeaponKind.Missile);
            if (index < 0) return false;

            _cooldownRemaining = _cooldown;
            if (!_weapons.IsBroken(index)) Place();
            return true;
        }

        /// <summary>
        /// クールタイムと Hatch の壊れ方に関わらず、プレイヤーの真上にユニットを 1 つ置く。
        /// </summary>
        /// <returns>置いたか。空きがないか、狙う相手がいなければ false</returns>
        public bool FireNow()
        {
            return Place();
        }

        /// <summary>
        /// 使っているユニット、デカール、ビームをすべて消す。
        /// </summary>
        public void StopAll()
        {
            for (var i = 0; i < _capacity; i++)
            {
                if (_isActive[i]) Release(i);
            }
        }

        private void Awake()
        {
            _weapons = GetComponent<BossWeapons>();
            _units = new Transform[_capacity];
            _warnings = new DecalProjector[_capacity];
            _pivots = new Vector3[_capacity];
            _warningRemaining = new float[_capacity];
            _isActive = new bool[_capacity];

            if (_unitPrefab == null)
            {
                UsefulLogger.LogWarning("降下フィニッシャーのユニットのプレハブが設定されていない為、ユニットは見えません。", this);
            }

            for (var i = 0; i < _capacity; i++)
            {
                // ボスが動いても位置が残るよう、ユニットとデカールはボスの子にせずシーンに置く
                if (_unitPrefab != null)
                {
                    _units[i] = Instantiate(_unitPrefab).transform;
                    _units[i].gameObject.SetActive(false);
                }

                var warning = new GameObject($"FinisherStrikeWarning{i}").AddComponent<DecalProjector>();
                warning.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                warning.material = _warningMaterial;
                warning.size = new Vector3(_warningRadius * 2f, _warningRadius * 2f, WARNING_DEPTH);
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
                if (!_isActive[i]) continue;

                if (_warningRemaining[i] > 0f)
                {
                    _warningRemaining[i] -= deltaTime;
                    if (_warningRemaining[i] > 0f) continue;

                    _warnings[i].enabled = false;
                    _shots[i].BeginFixed(Vector3.down);
                }

                if (!_shots[i].Tick(_pivots[i], deltaTime, out _)) Release(i);
            }
        }

        /// <summary>
        /// 空いているユニットを、プレイヤーのいる地点の地面から _height 上に置き、地面にデカールを出す。
        /// </summary>
        /// <returns>置いたか</returns>
        private bool Place()
        {
            if (_shots == null || _target == null) return false;

            for (var i = 0; i < _capacity; i++)
            {
                if (_isActive[i]) continue;

                var ground = GetGround();
                _pivots[i] = ground + Vector3.up * _height;
                _warningRemaining[i] = _warningDuration;
                _isActive[i] = true;
                if (_units[i] != null)
                {
                    _units[i].position = _pivots[i];
                    _units[i].gameObject.SetActive(true);
                }

                _warnings[i].transform.position = ground + Vector3.up * (WARNING_DEPTH * 0.5f);
                _warnings[i].enabled = true;
                return true;
            }

            return false;
        }

        private void Release(int i)
        {
            _isActive[i] = false;
            _shots[i].Stop();
            _warnings[i].enabled = false;
            if (_units[i] != null) _units[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// プレイヤーのいる地点の真下の地面。地面がなければプレイヤーの位置。
        /// </summary>
        private Vector3 GetGround()
        {
            var position = _target.position;
            var origin = position + Vector3.up * _groundRayHeight;
            return BossGroundProbe.TryGetGround(transform, origin, _groundRayLength, _groundLayers, out var ground)
                ? ground
                : position;
        }

        private void OnDestroy()
        {
            for (var i = 0; _warnings != null && i < _capacity; i++)
            {
                if (_warnings[i] != null) Destroy(_warnings[i].gameObject);
                if (_units[i] != null) Destroy(_units[i].gameObject);
            }
        }
    }
}
