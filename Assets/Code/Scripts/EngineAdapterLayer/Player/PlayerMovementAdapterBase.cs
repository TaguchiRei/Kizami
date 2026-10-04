using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// プレイヤーの移動と視点を Transform / Rigidbody へ反映する Abstractor の基底。
    /// 視点操作も移動の一種として同じコンポーネントが受け持つ。
    ///
    /// 水平移動の反映と、触れている物（PlayerContactState）の判定・書き込みは操作系によらず共通なのでここに置き、
    /// 視点入力をどう回転へ変換するか（何度回すか、上下を使うか、時間で積分するか）だけを
    /// 派生＝操作系ごとの実装が決める。
    /// PlayerContactState の具象インスタンスはこのクラスだけが保持する（Single Writer）。
    /// </summary>
    public abstract class PlayerMovementAdapterBase : InitializableMonoBehaviour
    {
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private CapsuleCollider _collider;

        [Header("接地の判定")]
        [SerializeField, Min(0f)]
        [Tooltip("カプセルの下端から、地面に触れているとみなす距離（m）")]
        private float _groundCheckDistance = 0.1f;

        [SerializeField]
        [Tooltip("地面として判定するレイヤー。プレイヤー自身のレイヤーは外す")]
        private LayerMask _groundLayers = ~0;

        [Header("壁の判定")]
        [SerializeField, Min(0f)]
        [Tooltip("カプセルの側面から、壁に触れているとみなす距離（m）")]
        private float _wallCheckDistance = 0.1f;

        [SerializeField]
        [Tooltip("壁走りのできる壁として判定するレイヤー")]
        private LayerMask _wallLayers;

        [Header("水平速度の補間レート（地上・壁走り中） (m/s^2)")]
        [SerializeField, Min(0f)] private float _acceleration = 40f;
        [SerializeField, Min(0f)] private float _deceleration = 60f;

        [Header("水平速度の補間レート（空中） (m/s^2)")]
        [SerializeField, Min(0f)] private float _airAcceleration = 10f;
        [SerializeField, Min(0f)] private float _airDeceleration = 10f;

        private readonly PlayerContactState _contactState = new();
        private readonly Collider[] _wallOverlapBuffer = new Collider[8];

        private IPlayerMovementState _movementState;
        private Func<Vector3, float, Vector3?> _step;
        private IDisposable _movementStateWaiter;
        private IDisposable _lookStateWaiter;
        private IDisposable _lookSubscription;
        private Vector3 _horizontalVelocity;

        /// <summary>
        /// PlayerInitializer から呼ばれる。State の登録順に依存しないよう待受で拾う。
        /// </summary>
        /// <param name="playerBoard">移動・視点ステートの取得元</param>
        /// <param name="step">FixedUpdate ごとに視線の向きと経過時間を渡して呼び、打ち出し速度を受け取る処理（PlayerMovementService.Step）</param>
        public void Initialize(PlayerBoard playerBoard, Func<Vector3, float, Vector3?> step)
        {
            _step = step;

            playerBoard.RegisterSceneState<IPlayerContactState>(_contactState, gameObject.scene.buildIndex);

            _movementStateWaiter = playerBoard.SubscribeStateRegister<IPlayerMovementState>(
                () =>
                {
                    if (playerBoard.TryGetSceneState<IPlayerMovementState>(out var state, out _))
                    {
                        _movementState = state;
                    }
                },
                invokeIfRegistered: true);

            _lookStateWaiter = playerBoard.SubscribeStateRegister<IPlayerLookState>(
                () =>
                {
                    if (!playerBoard.TryGetSceneState<IPlayerLookState>(out var state, out _)) return;

                    _lookSubscription?.Dispose();
                    _lookSubscription = state.RegisterOnLookInputChanged(OnLookInputChanged);
                },
                invokeIfRegistered: true);

            if (_rigidbody == null)
            {
                UsefulLogger.LogError("Rigidbody が設定されていません。", this);
            }

            if (_collider == null)
            {
                UsefulLogger.LogError("CapsuleCollider が設定されていません。", this);
            }

            // 派生の検証を通す為、base ではなく仮想メソッド側を呼ぶ
            Initialize();
        }

        /// <summary>
        /// 視点操作の入力値が変化した際に呼ばれる。
        /// </summary>
        /// <param name="lookInput">感度適用済みの入力値。x が右向き、y が上向きを正とする</param>
        protected abstract void OnLookInputChanged(Vector2 lookInput);

        /// <summary>
        /// ワールド空間の視線の向きを返す。既定ではこの Transform の前方を返す。
        /// </summary>
        protected virtual Vector3 GetViewDirection() => transform.forward;

        /// <summary>
        /// 1 ステップ分の処理を次の順で行う。
        /// 1. 直前の物理ステップの結果から触れている物を判定し、PlayerContactState に書き込む
        /// 2. 視線の向きと経過時間を渡して PlayerMovementService.Step を呼び、打ち出し速度を受け取る
        /// 3. 移動モードに応じて重力を切り替え、目標速度（PlayerMovementState.TargetVelocity の水平成分）へ向けて
        ///    水平速度を緩やかに補間し、Rigidbody に反映する
        /// Step が触れている物を読む為、1 は 2 より先に行う必要がある。
        /// 打ち出し速度があれば、Y 軸方向の速度をその Y 成分で置き換え、水平成分が 0 でなければ水平速度もそれで置き換える。
        /// 打ち出し速度がなければ、壁走り中は Y 軸方向の速度を 0 にし、それ以外は Rigidbody の現在値（重力等）をそのまま通す。
        /// </summary>
        private void FixedUpdate()
        {
            if (_rigidbody == null || _collider == null) return;

            UpdateContacts();

            var launchVelocity = _step?.Invoke(GetViewDirection(), Time.fixedDeltaTime);

            if (_movementState == null) return;

            var isWallRunning = _movementState.Mode == PlayerMoveMode.WallRunning;
            _rigidbody.useGravity = !isWallRunning;

            if (launchVelocity.HasValue)
            {
                var launchHorizontal = new Vector3(launchVelocity.Value.x, 0f, launchVelocity.Value.z);
                if (launchHorizontal.sqrMagnitude > 0f)
                {
                    _horizontalVelocity = launchHorizontal;
                }
            }

            var target = _movementState.TargetVelocity;
            target.y = 0f;

            var isGrounded = (_contactState.Contacts & PlayerContact.Ground) != 0;
            var useGroundRate = isGrounded || isWallRunning;
            var acceleration = useGroundRate ? _acceleration : _airAcceleration;
            var deceleration = useGroundRate ? _deceleration : _airDeceleration;

            // 目標へ近づく（加速）ときは加速レート、緩める・止める（減速）ときは減速レートを使う
            var rate = target.sqrMagnitude >= _horizontalVelocity.sqrMagnitude ? acceleration : deceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, target, rate * Time.fixedDeltaTime);

            var verticalSpeed = launchVelocity?.y ?? (isWallRunning ? 0f : _rigidbody.linearVelocity.y);
            _rigidbody.linearVelocity = new Vector3(_horizontalVelocity.x, verticalSpeed, _horizontalVelocity.z);
        }

        /// <summary>
        /// 地面と壁に触れているかを判定し、PlayerContactState に書き込む。
        /// </summary>
        private void UpdateContacts()
        {
            var colliderTransform = _collider.transform;
            var scale = colliderTransform.lossyScale;
            var radius = _collider.radius * Mathf.Max(scale.x, scale.z);
            var halfHeight = Mathf.Max(_collider.height * 0.5f * scale.y, radius);

            var center = colliderTransform.TransformPoint(_collider.center);
            var bottomSphereCenter = center + Vector3.down * (halfHeight - radius);
            var topSphereCenter = center + Vector3.up * (halfHeight - radius);

            var contacts = PlayerContact.None;

            if (IsGrounded(bottomSphereCenter, radius))
            {
                contacts |= PlayerContact.Ground;
            }

            if (TryFindWallNormal(center, bottomSphereCenter, topSphereCenter, radius, out var wallNormal))
            {
                contacts |= PlayerContact.Wall;
            }

            _contactState.SetContacts(contacts, wallNormal);
        }

        /// <summary>
        /// カプセルの下側の球を下向きに飛ばし、_groundCheckDistance 以内に地面があるかを判定する。
        /// </summary>
        private bool IsGrounded(Vector3 bottomSphereCenter, float radius)
        {
            // SphereCast は開始時点で重なっているコライダーを検出しない。
            // 落下中に 1 ステップで地面へめり込んでも取りこぼさないよう、球をカプセルより SkinWidth だけ小さくし、
            // 下側の球の中心から半径分だけ上を起点にして飛ばす（半径 + SkinWidth までのめり込みを許容する）
            const float SkinWidth = 0.05f;
            var castRadius = radius - SkinWidth;
            var castOrigin = bottomSphereCenter + Vector3.up * radius;
            var castDistance = radius + SkinWidth + _groundCheckDistance;

            return Physics.SphereCast(castOrigin, castRadius, Vector3.down, out _, castDistance,
                _groundLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// カプセルを _wallCheckDistance だけ太らせた範囲に _wallLayers のコライダーがあるかを調べ、
        /// あれば最も近い壁の、壁から離れる向きの水平な単位ベクトルを返す。
        /// 壁の面上の最も近い点は Collider.ClosestPoint で求める為、凸でない MeshCollider は判定できない。
        /// </summary>
        private bool TryFindWallNormal(Vector3 center, Vector3 bottomSphereCenter, Vector3 topSphereCenter,
            float radius, out Vector3 wallNormal)
        {
            wallNormal = Vector3.zero;

            var checkRadius = radius + _wallCheckDistance;
            var count = Physics.OverlapCapsuleNonAlloc(bottomSphereCenter, topSphereCenter, checkRadius,
                _wallOverlapBuffer, _wallLayers, QueryTriggerInteraction.Ignore);

            var nearestDistance = float.MaxValue;

            for (var i = 0; i < count; i++)
            {
                var toPlayer = center - _wallOverlapBuffer[i].ClosestPoint(center);
                toPlayer.y = 0f;

                // 壁の真上・真下にいる、または中心が壁の中にあるときは、水平な向きが決まらない為に除く
                var distance = toPlayer.magnitude;
                if (distance < 1e-4f || distance > checkRadius || distance >= nearestDistance) continue;

                nearestDistance = distance;
                wallNormal = toPlayer / distance;
            }

            return wallNormal != Vector3.zero;
        }

        protected virtual void OnDestroy()
        {
            _lookSubscription?.Dispose();
            _lookStateWaiter?.Dispose();
            _movementStateWaiter?.Dispose();
        }
    }
}
