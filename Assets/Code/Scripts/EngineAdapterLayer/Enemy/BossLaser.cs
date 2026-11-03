using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスの下部レーザーを扱う。BossWeapons と同じ GameObject に付ける。
    /// 撃つときは、まずコアを胴体の下へせり出させ、レーザーの武装の位置から地面すれすれの高さへ水平にレーザーを伸ばし、ゆっくり回す。
    /// 撃ち終えたらコアを戻す。レーザーは細く低いので、ジャンプで飛び越えられる。
    /// </summary>
    /// <remarks>
    /// 当たりは、レーザーの線分とプレイヤーの水平の距離が太さの半分以内で、プレイヤーの足元がレーザーの高さより低いとき。
    /// 当たり続けている間は、決めた間隔ごとにダメージを与える。
    /// </remarks>
    [RequireComponent(typeof(BossWeapons))]
    public sealed class BossLaser : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("当てる相手（プレイヤー）")]
        private Transform _target;

        [SerializeField]
        [Tooltip("せり出させるコアの部位")]
        private Transform _core;

        [SerializeField]
        [Tooltip("コアをせり出させる量（コアの親の空間、m）")]
        private Vector3 _coreExposeOffset = new(0f, -3f, 0f);

        [SerializeField, Min(0.01f)]
        [Tooltip("コアをせり出させる・戻すのにかける時間（秒）")]
        private float _coreMoveTime = 1f;

        [SerializeField]
        [Tooltip("レーザーの見た目のマテリアル")]
        private Material _beamMaterial;

        [SerializeField]
        [Tooltip("レーザーの高さを決める地面のレイヤー")]
        private LayerMask _groundLayers = ~0;

        [SerializeField, Min(0f)]
        [Tooltip("撃ち終えてから次に撃てるまでの時間（秒）。壊れたレーザーの番でも過ぎる")]
        private float _cooldown = 4f;

        [SerializeField, Min(0.1f)]
        [Tooltip("レーザーを撃ち続ける時間（秒）")]
        private float _duration = 6f;

        [SerializeField]
        [Tooltip("レーザーを回す速さ（度/s）。負なら逆回り")]
        private float _turnSpeed = 30f;

        [SerializeField, Min(0.1f)]
        [Tooltip("レーザーの長さ（m）")]
        private float _length = 30f;

        [SerializeField, Min(0.01f)]
        [Tooltip("レーザーの太さ（m）")]
        private float _width = 0.3f;

        [SerializeField, Min(0f)]
        [Tooltip("レーザーの、地面からの高さ（m）。プレイヤーの足元がこれより高ければ当たらない")]
        private float _height = 0.6f;

        [SerializeField, Min(0.1f)]
        [Tooltip("地面を探すレイを、武装の位置からこの高さ（m）上から下へ撃つ。コアをせり出させた武装が地面の下に入っても、地面を見つけられる")]
        private float _groundRayHeight = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーの Transform の位置から足元までの高さ（m）。足元の高さでレーザーに当たるかを決める")]
        private float _targetFootOffset = 1f;

        [SerializeField, Min(0.01f)]
        [Tooltip("当たり続けている間にダメージを与える間隔（秒）")]
        private float _damageInterval = 0.5f;

        private BossWeapons _weapons;
        private Action<int> _applyDamage;

        /// <summary> 当たったときのダメージ量 </summary>
        private int _damage;
        private LineRenderer _line;

        /// <summary> 休みの位置のコアの位置（コアの親の空間） </summary>
        private Vector3 _coreRestPosition;

        /// <summary> コアのせり出しの割合（0：休み、1：せり出し） </summary>
        private float _coreExposure;

        /// <summary> 撃っている武装の番号。撃っていなければ -1 </summary>
        private int _firingIndex = -1;

        /// <summary> レーザーを撃ち始めてからの時間（秒）。コアをせり出させている間は負 </summary>
        private float _firingTime;

        /// <summary> レーザーの向き（水平、ボスの根の前方からの角度、度） </summary>
        private float _angle;

        /// <summary> 次に撃てるまでの時間（秒） </summary>
        private float _cooldownRemaining;

        /// <summary> 次にダメージを与えられるまでの時間（秒） </summary>
        private float _damageCooldownRemaining;

        /// <summary> 撃ち始めてから撃ち終えるまでの時間（秒）。撃ち始めるたびに決める </summary>
        private float _currentDuration;

        /// <summary> コアを撃っていなくてもせり出したままにするか </summary>
        private bool _isCoreHeld;

        /// <summary> 撃っているか（コアをせり出させている間を含む） </summary>
        public bool IsFiring => _firingIndex >= 0;

        /// <summary> 撃っておらず、クールタイムが過ぎているか </summary>
        public bool IsReady => !IsFiring && _cooldownRemaining <= 0f;

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
        /// <param name="damage">当たったときのダメージ量</param>
        public void Initialize(Action<int> applyDamage, int damage)
        {
            _applyDamage = applyDamage;
            _damage = damage;
        }

        /// <summary>
        /// 撃っておらずクールタイムが過ぎていれば、次の番のレーザーを撃ち始める。
        /// 番のレーザーが壊れていれば、撃たずにクールタイムだけを発生させる。
        /// </summary>
        /// <returns>撃つ番を進めたか。撃っている間とクールタイム中は false</returns>
        public bool TryFire()
        {
            if (IsFiring || _cooldownRemaining > 0f) return false;

            var index = _weapons.TakeNext(BossWeaponKind.Laser);
            if (index < 0) return false;

            if (_weapons.IsBroken(index))
            {
                _cooldownRemaining = _cooldown;
                return true;
            }

            StartFiring(index, _duration);
            return true;
        }

        /// <summary>
        /// クールタイムと撃っているかを無視して、次の番のレーザーを duration 秒撃ち始める。壊れた番では撃たない。
        /// 総攻撃で、撃ち続けるために使う。
        /// </summary>
        /// <param name="duration">コアをせり出させたあとに撃ち続ける時間（秒）</param>
        /// <returns>撃ち始めたか。レーザーがない、または番のレーザーが壊れていれば false</returns>
        public bool FireFor(float duration)
        {
            var index = _weapons.TakeNext(BossWeaponKind.Laser);
            if (index < 0 || _weapons.IsBroken(index)) return false;

            StartFiring(index, duration);
            return true;
        }

        /// <summary>
        /// 撃っていれば撃つのをやめる。クールタイムは発生させない。
        /// </summary>
        public void Cancel()
        {
            _firingIndex = -1;
        }

        /// <summary>
        /// 撃っていないときもコアをせり出したままにするかを切り替える。隙を晒している間に、コアを見せるために使う。
        /// </summary>
        /// <param name="isHeld">せり出したままにするなら true</param>
        public void HoldCoreExposed(bool isHeld)
        {
            _isCoreHeld = isHeld;
        }

        private void Awake()
        {
            _weapons = GetComponent<BossWeapons>();
            if (_core != null) _coreRestPosition = _core.localPosition;

            if (_beamMaterial == null)
            {
                UsefulLogger.LogWarning("下部レーザーのマテリアルが設定されていない為、レーザーは見えません。", this);
            }

            _line = new GameObject("BossLaser").AddComponent<LineRenderer>();
            _line.transform.SetParent(transform, false);
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.widthMultiplier = _width;
            _line.sharedMaterial = _beamMaterial;
            _line.enabled = false;
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - deltaTime);
            _damageCooldownRemaining = Mathf.Max(0f, _damageCooldownRemaining - deltaTime);

            var isBeamOn = false;
            if (IsFiring)
            {
                _firingTime += deltaTime;
                if (_firingTime >= _currentDuration)
                {
                    _firingIndex = -1;
                    _cooldownRemaining = _cooldown;
                }
                else if (_firingTime >= 0f)
                {
                    _angle += _turnSpeed * deltaTime;
                    isBeamOn = true;
                }
            }

            MoveCore(IsFiring || _isCoreHeld, deltaTime);

            _line.enabled = isBeamOn;
            if (isBeamOn) UpdateBeam();
        }

        private void StartFiring(int index, float duration)
        {
            _firingIndex = index;
            _currentDuration = duration;
            _firingTime = -_coreMoveTime;
            _angle = 0f;
            _damageCooldownRemaining = 0f;
        }

        /// <summary>
        /// コアを、撃っている間はせり出し、撃っていなければ休みの位置へ動かす。
        /// </summary>
        private void MoveCore(bool isExposed, float deltaTime)
        {
            if (_core == null) return;

            _coreExposure = Mathf.MoveTowards(_coreExposure, isExposed ? 1f : 0f, deltaTime / _coreMoveTime);
            _core.localPosition = _coreRestPosition + _coreExposeOffset * Mathf.SmoothStep(0f, 1f, _coreExposure);
        }

        /// <summary>
        /// レーザーの線分を、武装の位置の真下の地面から高さの所に、今の向きで描き、プレイヤーに当たっていればダメージを与える。
        /// </summary>
        private void UpdateBeam()
        {
            var origin = _weapons.GetPosition(_firingIndex);
            if (BossGroundProbe.TryGetGround(transform, origin + Vector3.up * _groundRayHeight,
                    _groundRayHeight + 100f, _groundLayers, out var ground))
            {
                origin.y = ground.y + _height;
            }

            var forward = transform.forward;
            forward.y = 0f;
            var direction = Quaternion.AngleAxis(_angle, Vector3.up) *
                            (forward.sqrMagnitude > 0f ? forward.normalized : Vector3.forward);
            var end = origin + direction * _length;
            _line.SetPosition(0, origin);
            _line.SetPosition(1, end);

            if (_target == null || _damageCooldownRemaining > 0f) return;

            var player = _target.position;
            if (player.y - _targetFootOffset > origin.y) return;

            var toPlayer = new Vector3(player.x - origin.x, 0f, player.z - origin.z);
            var along = Mathf.Clamp(Vector3.Dot(toPlayer, direction), 0f, _length);
            if ((toPlayer - direction * along).sqrMagnitude > _width * _width * 0.25f) return;

            _damageCooldownRemaining = _damageInterval;
            _applyDamage?.Invoke(_damage);
        }
    }
}
