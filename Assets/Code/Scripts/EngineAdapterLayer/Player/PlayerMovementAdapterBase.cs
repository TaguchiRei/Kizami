using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// プレイヤーの移動と視点を Transform / Rigidbody へ反映する Adapter の基底。
    /// 水平移動の反映と、触れている物（PlayerContactState）の判定・書き込みはここで行い、
    /// 視線の向きの取り方と、視点入力を回転へ変換する方法だけを操作系ごとの派生が決める。
    /// </summary>
    public abstract class PlayerMovementAdapterBase : InitializableMonoBehaviour
    {
        private readonly PlayerContactState _contactState = new();
        private readonly Collider[] _wallOverlapBuffer = new Collider[8];

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

        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private CapsuleCollider _collider;

        [Header("水平速度の補間レート（地上・壁走り中） (m/s^2)")]
        [SerializeField, Min(0f)] private float _acceleration = 40f;
        [SerializeField, Min(0f)] private float _deceleration = 60f;

        [Header("水平速度の補間レート（空中） (m/s^2)")]
        [SerializeField, Min(0f)] private float _airAcceleration = 10f;
        [SerializeField, Min(0f)] private float _airDeceleration = 10f;

        private IPlayerMovementState _movementState;
        private Func<Vector3, float, Vector3?> _step;
        private IDisposable _lookSubscription;
        private Vector3 _horizontalVelocity;

        /// <summary> ワープを始める直前の水平速度。ワープが終わったときにこの値へ戻す </summary>
        private Vector3 _preWarpHorizontalVelocity;

        /// <summary> 直前の FixedUpdate で反映した移動モード </summary>
        private PlayerMoveMode _lastAppliedMode;

        /// <summary>
        /// PlayerMovementState と PlayerLookState の登録より後に呼ぶこと。
        /// </summary>
        /// <param name="blackBoard">PlayerContactState の登録先と、移動・視点ステートの取得元</param>
        /// <param name="step">FixedUpdate ごとに視線の向きと経過時間を渡して呼び、打ち出し速度を受け取る処理（PlayerMovementService.Step）</param>
        public void Initialize(IBlackBoard blackBoard, Func<Vector3, float, Vector3?> step)
        {
            if (_rigidbody == null || _collider == null)
            {
                UsefulLogger.LogError("Rigidbody または CapsuleCollider が設定されていません。", this);
                return;
            }

            if (!blackBoard.TryGetBoard<PlayerBoard>(out var playerBoard, this) ||
                !blackBoard.TryGetSceneState<PlayerBoard, IPlayerMovementState>(out _movementState, this) ||
                !blackBoard.TryGetSceneState<PlayerBoard, IPlayerLookState>(out var lookState, this)) return;

            _step = step;
            playerBoard.RegisterSceneState<IPlayerContactState>(_contactState, gameObject.scene.buildIndex);
            _lookSubscription = lookState.RegisterOnLookInputChanged(OnLookInputChanged);

            // 派生の検証を通す為、base ではなく仮想メソッド側を呼ぶ
            Initialize();
        }

        /// <summary>
        /// 視点操作の入力値が変化した際に呼ばれる。視点の回転をカメラ側で反映する操作系では何もしない。
        /// </summary>
        /// <param name="lookInput">感度適用済みの入力値。x が右向き、y が上向きを正とする</param>
        protected virtual void OnLookInputChanged(Vector2 lookInput)
        {
        }

        /// <summary>
        /// ワールド空間の視線の向きを返す。既定ではこの Transform の前方を返す。
        /// </summary>
        protected virtual Vector3 GetViewDirection() => transform.forward;

        protected virtual void OnDestroy()
        {
            _lookSubscription?.Dispose();
        }

        /// <summary>
        /// 1 ステップ分の処理を次の順で行う。
        /// 1. 直前の物理ステップの結果から触れている物を判定し、PlayerContactState に書き込む
        /// 2. 視線の向きと経過時間を渡して PlayerMovementService.Step を呼び、打ち出し速度を受け取る
        /// 3. 移動モードに応じて重力を切り替え、目標速度（PlayerMovementState.TargetVelocity の水平成分）へ向けて
        ///    水平速度を緩やかに補間し、Rigidbody に反映する
        /// Step が触れている物を読むので、1 は 2 より先に行う。
        /// ワープ中は目標速度（Y 成分を含む）をそのまま Rigidbody に設定し、
        /// ワープが終わったステップでは水平速度をワープを始める直前の値に戻してから補間する。
        /// </summary>
        private void FixedUpdate()
        {
            UpdateContacts();

            var launchVelocity = _step?.Invoke(GetViewDirection(), Time.fixedDeltaTime);

            var mode = _movementState.Mode;
            var previousMode = _lastAppliedMode;
            _lastAppliedMode = mode;

            if (mode == PlayerMoveMode.Warping)
            {
                if (previousMode != PlayerMoveMode.Warping)
                {
                    _preWarpHorizontalVelocity = _horizontalVelocity;
                }

                _rigidbody.useGravity = false;
                _rigidbody.linearVelocity = _movementState.TargetVelocity;
                return;
            }

            var hasWarpEnded = previousMode == PlayerMoveMode.Warping;
            if (hasWarpEnded)
            {
                _horizontalVelocity = _preWarpHorizontalVelocity;
            }

            var isWallRunning = mode == PlayerMoveMode.WallRunning;
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

            var verticalSpeed = launchVelocity?.y ??
                                (isWallRunning || hasWarpEnded ? 0f : _rigidbody.linearVelocity.y);
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
            // 落下中に 1 ステップで地面へめり込んでも取りこぼさないよう、球をカプセルより SKIN_WIDTH だけ小さくし、
            // 下側の球の中心から半径分だけ上を起点にして飛ばす（半径 + SKIN_WIDTH までのめり込みを許容する）
            const float SKIN_WIDTH = 0.05f;
            var castRadius = radius - SKIN_WIDTH;
            var castOrigin = bottomSphereCenter + Vector3.up * radius;
            var castDistance = radius + SKIN_WIDTH + _groundCheckDistance;

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
    }
}
