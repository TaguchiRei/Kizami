using System;
using Kizami.EngineAdapter.Voxel;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 直接戦闘の専用の攻撃。円形の空間の外の上空から、プレイヤーのいる地点へ細長いボクセルの塔を斜めに投げる。ボスの根に付ける。
    /// 塔を出す場所は、円の中心からプレイヤーへ引いた線の反対側。出した場所からプレイヤーのいる地点の地面まで空中に予測線を引き、警告の時間のあと、その線に沿って一定の速さで飛ぶ。追尾はしない。
    /// 飛んでいる塔がプレイヤーに触れたら一度だけ大ダメージを与えるが、塔は消えず、そのまま地面に斜めに刺さる。
    /// 刺さった塔の根元をプレイヤーが削ると倒れ始め（ゆっくり落ちる）、ボスに触れたらボスにダメージを与えて砕けて消える。
    /// </summary>
    /// <remarks>
    /// 塔は同時に1本だけ。次に投げるときに、前の塔が残っていれば消す。
    /// 塔の品質設定とマテリアルが未設定なら、ボスの最初のパーツのものを使う。
    /// </remarks>
    public sealed class BossTowerThrow : MonoBehaviour
    {
        /// <summary> 塔の密度（kg/m³） </summary>
        private const float DENSITY = 500f;

        /// <summary> 刺さった塔の根元とみなす点の、地面からの高さ（m）。ここが削れたら倒れる </summary>
        private const float ROOT_HEIGHT = 0.5f;

        /// <summary> プレイヤーの当たりの中心の、足元からの高さ（m） </summary>
        private const float PLAYER_CENTER_HEIGHT = 1f;

        /// <summary> 予測線の幅（m） </summary>
        private const float LINE_WIDTH = 1.2f;

        /// <summary> 塔のボリュームの範囲を、形より広げる余白（m）。法線を整える </summary>
        private const float VOLUME_PADDING = 1f;

        private static readonly Color _lineColor = new(1f, 0.1f, 0.05f, 0.6f);

        [SerializeField]
        [Tooltip("塔のボクセルの品質設定。未設定ならボスの最初のパーツのもの")]
        private VoxelQualitySettings _quality;

        [SerializeField]
        [Tooltip("塔のマテリアル。未設定ならボスの最初のパーツのもの")]
        private Material _material;

        [SerializeField]
        [Tooltip("予測線のマテリアル。未設定なら単色の線にする")]
        private Material _lineMaterial;

        [SerializeField]
        [Tooltip("狙う地点の地面を探すレイヤー")]
        private LayerMask _groundLayers = ~0;

        [SerializeField, Min(0.5f)]
        [Tooltip("塔の太さ（m）")]
        private float _width = 3f;

        [SerializeField, Min(1f)]
        [Tooltip("塔の長さ（m）")]
        private float _length = 30f;

        [SerializeField, Min(0f)]
        [Tooltip("塔の先端を出す、プレイヤーの足元の地面からの高さ（m）")]
        private float _spawnHeight = 50f;

        [SerializeField, Min(0f)]
        [Tooltip("塔を出す場所の、円の外周からの余白（m）")]
        private float _spawnMargin = 15f;

        [SerializeField, Min(0f)]
        [Tooltip("塔を出して予測線を見せてから、飛び始めるまでの時間（秒）")]
        private float _warningDuration = 2.5f;

        [SerializeField, Min(1f)]
        [Tooltip("塔が飛ぶ速さ（m/s）")]
        private float _speed = 35f;

        [SerializeField, Min(0f)]
        [Tooltip("先端が地面に刺さる深さ（m）")]
        private float _embedDepth = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("塔の幅に足す、プレイヤーとの当たりの余白（m）")]
        private float _hitMargin = 1f;

        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("倒れている間の重力の倍率。小さいほどゆっくり倒れる")]
        private float _toppleGravityScale = 0.35f;

        [SerializeField, Min(1f)]
        [Tooltip("倒れてから、ボスに触れなくても消すまでの時間（秒）")]
        private float _toppledLifetime = 20f;

        private Transform _target;
        private Action<int> _applyDamage;

        /// <summary> 飛んでいる塔がプレイヤーに触れたときのダメージ量 </summary>
        private int _playerDamage;

        /// <summary> 倒れた塔がボスに触れたときの、ボスの体力のダメージ量 </summary>
        private float _bossDamage;
        private BossArena _arena;
        private HealthTarget _boss;
        private GameObject _towerObject;
        private VoxelPiece _piece;
        private Rigidbody _body;
        private LineRenderer _line;
        private Material _fallbackMaterial;
        private TowerState _state = TowerState.Idle;
        private Vector3 _spawnTip;
        private Vector3 _direction;
        private Vector3 _groundPoint;
        private float _travel;
        private float _traveled;
        private float _timer;
        private bool _hasHitPlayer;

        /// <summary>
        /// 狙う相手と、プレイヤーにダメージを与える関数、円形の空間を受け取る。
        /// </summary>
        /// <param name="target">狙う相手（プレイヤー）</param>
        /// <param name="applyDamage">プレイヤーにダメージを与える関数（PlayerHealthService.ApplyDamage）</param>
        /// <param name="playerDamage">飛んでいる塔がプレイヤーに触れたときのダメージ量</param>
        /// <param name="bossDamage">倒れた塔がボスに触れたときの、ボスの体力のダメージ量</param>
        /// <param name="arena">塔を出す向きと距離の基準にする円形の空間。なければボスの位置を中心にする</param>
        public void Initialize(Transform target, Action<int> applyDamage, int playerDamage, float bossDamage, BossArena arena)
        {
            _target = target;
            _applyDamage = applyDamage;
            _playerDamage = playerDamage;
            _bossDamage = bossDamage;
            _arena = arena;
            _boss = GetComponent<HealthTarget>();
        }

        /// <summary>
        /// 前の塔を消し、プレイヤーの今いる地点を狙って新しい塔を出す。
        /// </summary>
        /// <returns>出せたか。狙う相手がいないか、塔を作れなければ false</returns>
        public bool Throw()
        {
            Clear();
            if (_target == null) return false;

            var center = _arena != null ? _arena.Center : transform.position;
            var radius = _arena != null ? _arena.Radius : 30f;
            var offset = _target.position - center;
            offset.y = 0f;
            var outward = offset.sqrMagnitude > 1f ? offset.normalized : Vector3.forward;

            _groundPoint = GetGround(_target.position);
            var spawn = center - outward * (radius + _spawnMargin);
            _spawnTip = new Vector3(spawn.x, _groundPoint.y + _spawnHeight, spawn.z);
            _direction = (_groundPoint - _spawnTip).normalized;
            _travel = Vector3.Distance(_spawnTip, _groundPoint + _direction * _embedDepth);
            _traveled = 0f;
            _hasHitPlayer = false;

            if (!BuildTower()) return false;

            BuildLine();
            _timer = _warningDuration;
            _state = TowerState.Warning;
            return true;
        }

        /// <summary>
        /// 塔と予測線を消す。
        /// </summary>
        public void Clear()
        {
            if (_towerObject != null) Destroy(_towerObject);
            if (_line != null) _line.enabled = false;
            _towerObject = null;
            _piece = null;
            _body = null;
            _state = TowerState.Idle;
        }

        private void Update()
        {
            switch (_state)
            {
                case TowerState.Warning:
                    TickWarning();
                    break;
                case TowerState.Flying:
                    TickFlying();
                    break;
                case TowerState.Stuck:
                    TickStuck();
                    break;
                case TowerState.Toppled:
                    TickToppled();
                    break;
            }
        }

        private void FixedUpdate()
        {
            if (_state == TowerState.Toppled && _body != null)
            {
                _body.AddForce(Physics.gravity * _toppleGravityScale, ForceMode.Acceleration);
            }
        }

        private void OnDestroy()
        {
            Clear();
            if (_line != null) Destroy(_line.gameObject);
            if (_fallbackMaterial != null) Destroy(_fallbackMaterial);
        }

        private void TickWarning()
        {
            if (!HasTower()) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f) _state = TowerState.Flying;
        }

        /// <summary>
        /// 予測線に沿って進み、触れたプレイヤーに一度だけダメージを与える。刺さる深さまで進んだら止まる。
        /// </summary>
        private void TickFlying()
        {
            if (!HasTower()) return;

            _traveled = Mathf.Min(_travel, _traveled + _speed * Time.deltaTime);
            var tip = _spawnTip + _direction * _traveled;
            PlaceTower(tip);

            if (!_hasHitPlayer && IsPlayerTouching(tip))
            {
                _hasHitPlayer = true;
                _applyDamage?.Invoke(_playerDamage);
            }

            if (_traveled < _travel) return;

            _line.enabled = false;
            _state = TowerState.Stuck;
        }

        /// <summary>
        /// 根元が削れて空になったら倒れ始める。
        /// </summary>
        private void TickStuck()
        {
            if (!HasTower()) return;

            var rootPoint = _groundPoint - _direction * ROOT_HEIGHT;
            if (_piece.Contains(rootPoint)) return;

            _body.isKinematic = false;
            _body.useGravity = false;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _timer = _toppledLifetime;
            _state = TowerState.Toppled;
        }

        private void TickToppled()
        {
            if (!HasTower()) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f) Clear();
        }

        /// <summary>
        /// 倒れた塔がボスに触れたら、ボスにダメージを与えて砕く。
        /// </summary>
        private void OnTowerCollision(Collision collision)
        {
            if (_state != TowerState.Toppled || _boss == null) return;
            if (collision.collider.GetComponentInParent<HealthTarget>() != _boss) return;

            _boss.ReportDamage(_bossDamage);
            SpawnBurst(_towerObject.transform.position);
            Clear();
        }

        /// <summary>
        /// 塔がまだあるか。プレイヤーに削られて中身がなくなったり、別の理由で消えたりしたら、攻撃を終える。
        /// </summary>
        private bool HasTower()
        {
            if (_towerObject != null && _piece != null) return true;

            Clear();
            return false;
        }

        /// <summary>
        /// 先端が tip の位置になるように、塔を置く。
        /// </summary>
        private void PlaceTower(Vector3 tip)
        {
            _towerObject.transform.SetPositionAndRotation(tip - _direction * (_length * 0.5f),
                Quaternion.LookRotation(_direction));
        }

        /// <summary>
        /// 塔の先端から後ろの線分と、プレイヤーの中心が、塔の幅の半分と余白より近いか。
        /// </summary>
        private bool IsPlayerTouching(Vector3 tip)
        {
            var tail = tip - _direction * _length;
            var player = _target.position + Vector3.up * PLAYER_CENTER_HEIGHT;
            var segment = tip - tail;
            var along = Mathf.Clamp01(Vector3.Dot(player - tail, segment) / segment.sqrMagnitude);
            var reach = _width * 0.5f + _hitMargin;
            return (tail + segment * along - player).sqrMagnitude <= reach * reach;
        }

        /// <summary>
        /// 細長い角柱のボクセルを作り、飛ばす向きに合わせて置く。動かすのは Transform で、倒れるまで Rigidbody は動かさない。
        /// </summary>
        private bool BuildTower()
        {
            var source = GetComponentInChildren<VoxelPiece>(true);
            var quality = _quality != null ? _quality : source != null ? source.Quality : null;
            var material = _material != null ? _material : source != null ? GetMaterial(source) : null;
            if (quality == null)
            {
                UsefulLogger.LogError("塔の品質設定がなく、ボスにもパーツがない為、塔を投げられません。", this);
                return false;
            }

            _towerObject = new GameObject("BossTower") { layer = gameObject.layer };
            _piece = _towerObject.AddComponent<VoxelPiece>();
            _piece.SetQuality(quality);
            _piece.SetMaterial(material);
            _piece.SetColliderMode(VoxelColliderMode.ConvexHull);
            _piece.SetCutByMelee(true);

            var half = new float3(_width * 0.5f, _width * 0.5f, _length * 0.5f);
            _piece.CreateVolume(VoxelBounds.FromCenterExtents(float3.zero, half + VOLUME_PADDING));
            _piece.ApplyEdit(new BoxShape(float3.zero, half), VoxelCsgOperation.Union);
            _piece.FlushRemesh();

            // 凸包のコライダーを作ってから Rigidbody を付け、質量の中心と慣性を形状から求めさせる
            _body = _towerObject.AddComponent<Rigidbody>();
            _body.mass = _width * _width * _length * DENSITY;
            _body.isKinematic = true;
            _towerObject.AddComponent<BossTowerCollisionRelay>().Initialize(OnTowerCollision);
            PlaceTower(_spawnTip);
            return true;
        }

        /// <summary>
        /// 予測線を、出した場所から狙う地点の地面まで引く。
        /// </summary>
        private void BuildLine()
        {
            if (_line == null)
            {
                var lineObject = new GameObject("BossTowerWarningLine");
                _line = lineObject.AddComponent<LineRenderer>();
                _line.positionCount = 2;
                _line.widthMultiplier = LINE_WIDTH;
                _line.startColor = _lineColor;
                _line.endColor = _lineColor;
                _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _line.sharedMaterial = _lineMaterial != null ? _lineMaterial : GetFallbackMaterial();
            }

            _line.SetPosition(0, _spawnTip);
            _line.SetPosition(1, _groundPoint);
            _line.enabled = true;
        }

        /// <summary>
        /// 砕けた破片の粒を、position に一度だけ出す。
        /// </summary>
        private void SpawnBurst(Vector3 position)
        {
            var burstObject = new GameObject("BossTowerBurst");
            burstObject.transform.position = position;
            var particles = burstObject.AddComponent<ParticleSystem>();

            var main = particles.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = 1.5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.5f);
            main.gravityModifier = 1f;
            main.startColor = new Color(0.55f, 0.5f, 0.45f, 1f);

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _width;

            burstObject.GetComponent<ParticleSystemRenderer>().sharedMaterial = GetFallbackMaterial();
            particles.Play();
            Destroy(burstObject, 3f);
        }

        private Material GetFallbackMaterial()
        {
            if (_fallbackMaterial == null) _fallbackMaterial = new Material(Shader.Find("Sprites/Default"));
            return _fallbackMaterial;
        }

        private static Material GetMaterial(VoxelPiece source)
        {
            if (source.Material != null) return source.Material;

            return source.Materials.Count > 0 ? source.Materials[0] : null;
        }

        /// <summary>
        /// 位置 position の真下の地面。地面がなければ position。
        /// </summary>
        private Vector3 GetGround(Vector3 position)
        {
            return BossGroundProbe.TryGetGround(transform, position + Vector3.up * 5f, 200f, _groundLayers,
                out var ground)
                ? ground
                : position;
        }

        private enum TowerState
        {
            Idle,
            Warning,
            Flying,
            Stuck,
            Toppled
        }
    }
}
