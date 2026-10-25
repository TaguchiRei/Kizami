using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスの頭部のレーザーを扱う。ボスの根に付ける。
    /// 撃つときは、まず近くの敵を吸収しながら頭部の部位を展開し、プレイヤーを狙って細い予兆の線を出し、向きを固定してレーザーを撃つ。撃ち終えたら頭部を畳む。
    /// レーザーは最初に当たった物で止まり、ボクセルなら少しずつ溶かして奥へ進む（区間23のフィニッシャーのビームと同じ溶かし方）。射線上の敵は一撃で倒れ、エネルギーになる。
    /// </summary>
    /// <remarks>
    /// 射線はボス自身のコライダーを数えない。プレイヤーへのダメージは、射線の線分とプレイヤーの狙う点の距離がレーザーの半径以内のとき、間隔ごとに与える。
    /// 展開は、部位ごとに休みの姿勢から決めた位置と回転の差だけ動かす。
    /// </remarks>
    public sealed class BossHeadLaser : MonoBehaviour
    {
        /// <summary> 射線を調べるレイで受け取る当たりの数。ボスのボクセルのチャンクが並ぶので多めに取る </summary>
        private const int HIT_BUFFER_SIZE = 64;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HIT_BUFFER_SIZE];

        [SerializeField]
        [Tooltip("狙う相手（プレイヤー）")]
        private Transform _target;

        [SerializeField]
        [Tooltip("レーザーを撃つ所")]
        private Transform _muzzle;

        [SerializeField]
        [Tooltip("撃つ前に展開する頭部の部位")]
        private UnfoldPart[] _unfoldParts = Array.Empty<UnfoldPart>();

        [SerializeField]
        [Tooltip("レーザーと予兆の線のマテリアル")]
        private Material _beamMaterial;

        [SerializeField]
        [Tooltip("レーザーを止める物のレイヤー。プレイヤー自身のレイヤーは外す")]
        private LayerMask _blockLayers = ~0;

        [SerializeField, Min(0f)]
        [Tooltip("撃ち終えてから次に撃てるまでの時間（秒）")]
        private float _cooldown = 8f;

        [SerializeField, Min(0.01f)]
        [Tooltip("頭部を展開する・畳むのにかける時間（秒）")]
        private float _unfoldTime = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("展開の始めに敵を吸収する、ボスからの半径（m）")]
        private float _absorbRadius = 60f;

        [SerializeField, Min(0f)]
        [Tooltip("予兆の線を出してプレイヤーを狙う時間（秒）")]
        private float _aimTime = 2f;

        [SerializeField, Min(0.1f)]
        [Tooltip("レーザーを撃ち続ける時間（秒）")]
        private float _fireDuration = 3f;

        [SerializeField, Min(0.1f)]
        [Tooltip("レーザーの半径（m）")]
        private float _beamRadius = 1.5f;

        [SerializeField, Min(1f)]
        [Tooltip("何にも当たらないときのレーザーの長さ（m）")]
        private float _maxLength = 300f;

        [SerializeField]
        [Tooltip("狙う所の、プレイヤーの位置からの高さ（m）")]
        private float _aimHeight = 1f;

        [SerializeField, Min(0.01f)]
        [Tooltip("当たり続けている間にダメージを与える間隔（秒）")]
        private float _damageInterval = 0.5f;

        [SerializeField, Min(0.1f)]
        [Tooltip("射線上の敵を倒す、射線からの半径（m）")]
        private float _defeatRadius = 3f;

        [SerializeField, Min(0.01f)]
        [Tooltip("射線上の敵を倒す間隔（秒）")]
        private float _defeatInterval = 0.1f;

        [SerializeField, Min(0f)]
        [Tooltip("ボクセルが溶けて先端が奥へ進む速さ（m/s）")]
        private float _meltSpeed = 4f;

        [SerializeField, Min(0.1f)]
        [Tooltip("溶かす穴の半径（m）")]
        private float _meltRadius = 3f;

        [SerializeField, Min(0.01f)]
        [Tooltip("溶かす強さが 1m あたりに減る量")]
        private float _meltFalloff = 0.5f;

        [SerializeField, Min(0.01f)]
        [Tooltip("ボクセルを溶かす間隔（秒）")]
        private float _meltInterval = 0.05f;

        private Action<int> _applyDamage;

        /// <summary> 当たったときのダメージ量 </summary>
        private int _damage;
        private Func<Vector3, float, int> _absorbInRadius;
        private Action<Vector3, float> _defeatInRadius;
        private LineRenderer _line;
        private Phase _phase;

        /// <summary> 部位ごとの、休みの姿勢の位置（親の空間）。並びは _unfoldParts と同じ </summary>
        private Vector3[] _restPositions;

        /// <summary> 部位ごとの、休みの姿勢の回転（親の空間）。並びは _unfoldParts と同じ </summary>
        private Quaternion[] _restRotations;

        /// <summary> 頭部の展開の割合（0：休み、1：展開） </summary>
        private float _unfold;

        /// <summary> 今の段階に入ってからの時間（秒） </summary>
        private float _phaseTime;

        /// <summary> レーザーの向き（単位ベクトル）。予兆の終わりで固定する </summary>
        private Vector3 _direction = Vector3.forward;

        /// <summary> 次に撃てるまでの時間（秒） </summary>
        private float _cooldownRemaining;

        private float _damageTimer;
        private float _defeatTimer;
        private float _meltTimer;

        /// <summary> 撃っているか（展開から畳み終えるまで） </summary>
        public bool IsFiring => _phase != Phase.Idle;

        /// <summary>
        /// 狙う相手を target にする。ボスとプレイヤーが別のシーンにあるときに、初期化で渡す。
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        /// <summary>
        /// ダメージの関数と、敵を吸収する関数・倒す関数を受け取る。
        /// </summary>
        /// <param name="applyDamage">プレイヤーにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        /// <param name="damage">当たったときのダメージ量</param>
        /// <param name="absorbInRadius">球の中の敵を吸収して消す関数（EnemySpawnAdapter.AbsorbInRadius）。引数は中心と半径</param>
        /// <param name="defeatInRadius">球の中の敵を倒してエネルギーを出す関数（EnemySpawnAdapter.DefeatInRadius）。引数は中心と半径</param>
        public void Initialize(Action<int> applyDamage, int damage, Func<Vector3, float, int> absorbInRadius,
            Action<Vector3, float> defeatInRadius)
        {
            _applyDamage = applyDamage;
            _damage = damage;
            _absorbInRadius = absorbInRadius;
            _defeatInRadius = defeatInRadius;
        }

        /// <summary>
        /// 撃っておらずクールタイムが過ぎていれば、近くの敵を吸収して頭部を展開し始める。
        /// </summary>
        /// <returns>撃ち始めたか</returns>
        public bool TryFire()
        {
            if (IsFiring || _cooldownRemaining > 0f || _target == null || _muzzle == null) return false;

            _absorbInRadius?.Invoke(transform.position, _absorbRadius);
            SetPhase(Phase.Unfolding);
            return true;
        }

        /// <summary>
        /// 撃っていれば、レーザーを止めて頭部を畳む。クールタイムは発生させない。
        /// </summary>
        public void Cancel()
        {
            if (!IsFiring) return;

            _line.enabled = false;
            SetPhase(Phase.Folding);
        }

        private void Awake()
        {
            _restPositions = new Vector3[_unfoldParts.Length];
            _restRotations = new Quaternion[_unfoldParts.Length];
            for (var i = 0; i < _unfoldParts.Length; i++)
            {
                var part = _unfoldParts[i].Part;
                if (part == null) continue;

                _restPositions[i] = part.localPosition;
                _restRotations[i] = part.localRotation;
            }

            if (_beamMaterial == null)
            {
                UsefulLogger.LogWarning("頭部のレーザーのマテリアルが設定されていない為、レーザーは見えません。", this);
            }

            _line = new GameObject("BossHeadLaser").AddComponent<LineRenderer>();
            _line.transform.SetParent(transform, false);
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.sharedMaterial = _beamMaterial;
            _line.enabled = false;
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - deltaTime);
            _phaseTime += deltaTime;

            switch (_phase)
            {
                case Phase.Unfolding:
                    if (_phaseTime >= _unfoldTime) SetPhase(Phase.Aiming);
                    break;
                case Phase.Aiming:
                    Aim();
                    if (_phaseTime >= _aimTime) StartFiring();
                    break;
                case Phase.Firing:
                    UpdateFiring(deltaTime);
                    if (_phaseTime >= _fireDuration)
                    {
                        _line.enabled = false;
                        _cooldownRemaining = _cooldown;
                        SetPhase(Phase.Folding);
                    }

                    break;
                case Phase.Folding:
                    if (_phaseTime >= _unfoldTime) SetPhase(Phase.Idle);
                    break;
            }

            var isUnfolded = _phase is Phase.Unfolding or Phase.Aiming or Phase.Firing;
            _unfold = Mathf.MoveTowards(_unfold, isUnfolded ? 1f : 0f, deltaTime / _unfoldTime);
            PoseUnfoldParts(Mathf.SmoothStep(0f, 1f, _unfold));
        }

        /// <summary>
        /// プレイヤーの狙う所へ向きを合わせ、撃つ所から最初に当たる物まで細い予兆の線を出す。
        /// </summary>
        private void Aim()
        {
            var toAim = GetAimPosition() - _muzzle.position;
            if (toAim.sqrMagnitude > 1e-6f) _direction = toAim.normalized;

            var stop = CastBeam(out _);
            _line.widthMultiplier = 0.1f;
            _line.SetPosition(0, _muzzle.position);
            _line.SetPosition(1, stop);
            _line.enabled = true;
        }

        private void StartFiring()
        {
            _damageTimer = 0f;
            _defeatTimer = 0f;
            _meltTimer = 0f;
            SetPhase(Phase.Firing);
        }

        /// <summary>
        /// 固定した向きへレーザーを出し、当たった物がボクセルなら溶かす。間隔ごとに射線上の敵を倒し、プレイヤーが射線にいればダメージを与える。
        /// </summary>
        private void UpdateFiring(float deltaTime)
        {
            var origin = _muzzle.position;
            var stop = CastBeam(out var hitCollider);
            _line.widthMultiplier = _beamRadius * 2f;
            _line.SetPosition(0, origin);
            _line.SetPosition(1, stop);
            _line.enabled = true;

            _meltTimer -= deltaTime;
            if (_meltTimer <= 0f)
            {
                _meltTimer += _meltInterval;
                VoxelBeamMelt.Melt(hitCollider, stop, _direction, _meltRadius, _meltSpeed * _meltInterval, _meltFalloff);
            }

            _defeatTimer -= deltaTime;
            if (_defeatTimer <= 0f)
            {
                _defeatTimer = _defeatInterval;
                DefeatAlong(origin, stop);
            }

            _damageTimer -= deltaTime;
            if (_damageTimer > 0f || _target == null) return;

            var aim = GetAimPosition();
            var segment = stop - origin;
            var along = Mathf.Clamp01(Vector3.Dot(aim - origin, segment) / Mathf.Max(segment.sqrMagnitude, 1e-6f));
            if ((origin + segment * along - aim).sqrMagnitude > _beamRadius * _beamRadius) return;

            _damageTimer = _damageInterval;
            _applyDamage?.Invoke(_damage);
        }

        /// <summary>
        /// 射線に沿って、倒す半径の間隔で球を並べ、球の中の敵を倒す。
        /// </summary>
        private void DefeatAlong(Vector3 origin, Vector3 stop)
        {
            if (_defeatInRadius == null) return;

            var length = Vector3.Distance(origin, stop);
            var count = Mathf.Max(1, Mathf.CeilToInt(length / _defeatRadius));
            for (var i = 0; i <= count; i++)
            {
                _defeatInRadius(Vector3.Lerp(origin, stop, (float)i / count), _defeatRadius);
            }
        }

        /// <summary>
        /// 撃つ所から今の向きへ調べ、ボス自身のコライダーを除いて最初に当たった物の位置を返す。当たらなければ最大の長さの先。
        /// </summary>
        /// <param name="hitCollider">当たったコライダー。当たらなければ null</param>
        private Vector3 CastBeam(out Collider hitCollider)
        {
            var origin = _muzzle.position;
            var count = Physics.RaycastNonAlloc(origin, _direction, _hitBuffer, _maxLength, _blockLayers,
                QueryTriggerInteraction.Ignore);

            hitCollider = null;
            var nearest = _maxLength;
            var point = origin + _direction * _maxLength;
            for (var i = 0; i < count; i++)
            {
                var hit = _hitBuffer[i];
                if (hit.distance >= nearest || hit.collider.transform.IsChildOf(transform)) continue;

                nearest = hit.distance;
                point = hit.point;
                hitCollider = hit.collider;
            }

            return point;
        }

        private Vector3 GetAimPosition()
        {
            return _target.position + Vector3.up * _aimHeight;
        }

        /// <summary>
        /// 頭部の部位を、休みの姿勢から展開の割合 amount だけ動かす。
        /// </summary>
        private void PoseUnfoldParts(float amount)
        {
            for (var i = 0; i < _unfoldParts.Length; i++)
            {
                var unfoldPart = _unfoldParts[i];
                if (unfoldPart.Part == null) continue;

                unfoldPart.Part.localPosition = _restPositions[i] + unfoldPart.Offset * amount;
                unfoldPart.Part.localRotation = _restRotations[i] * Quaternion.Euler(unfoldPart.EulerAngles * amount);
            }
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            _phaseTime = 0f;
        }

        /// <summary>
        /// 展開する頭部の部位 1 つと、展開したときの休みの姿勢からの差。
        /// </summary>
        [Serializable]
        private sealed class UnfoldPart
        {
            [Tooltip("展開する部位")]
            public Transform Part;

            [Tooltip("展開したときの位置の差（親の空間、m）")]
            public Vector3 Offset;

            [Tooltip("展開したときの回転の差（部位の空間、度）")]
            public Vector3 EulerAngles;
        }

        /// <summary>
        /// 頭部のレーザーの段階
        /// </summary>
        private enum Phase
        {
            /// <summary> 撃っていない </summary>
            Idle,

            /// <summary> 頭部を展開している </summary>
            Unfolding,

            /// <summary> 予兆の線を出して狙っている </summary>
            Aiming,

            /// <summary> レーザーを撃っている </summary>
            Firing,

            /// <summary> 頭部を畳んでいる </summary>
            Folding
        }
    }
}
