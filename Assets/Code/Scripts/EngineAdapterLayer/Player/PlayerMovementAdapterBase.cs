using System;
using Kizami.BlackBoard;
using Kizami.EngineAdapter.Voxel;
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
        /// <summary> 壁を探すカプセルキャストを飛ばす水平な向きの数 </summary>
        private const int WALL_CAST_DIRECTION_COUNT = 8;

        /// <summary> カプセルキャストで壁を探すときに、カプセルを細くして重なりを許容する幅（m） </summary>
        private const float WALL_SKIN_WIDTH = 0.05f;

        /// <summary> 壁とみなす面の法線の Y 成分の絶対値の上限。これを超える面は床や天井に近い </summary>
        private const float MAX_WALL_NORMAL_Y = 0.5f;

        private readonly PlayerContactState _contactState = new();
        private readonly RaycastHit[] _wallHitBuffer = new RaycastHit[16];

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

        [SerializeField, Min(0f)]
        [Tooltip("カプセルの側面から、ガラスの板に触れて割るとみなす距離（m）")]
        private float _glassContactDistance = 0.1f;

        [SerializeField]
        [Tooltip("壁走りのできる壁として判定するレイヤー。ボクセルと Brilliant Skill の言葉は、レイヤーによらず壁として判定する")]
        private LayerMask _wallLayers;

        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private CapsuleCollider _collider;

        [Header("空中の水平速度")]
        [SerializeField, Min(0f)]
        [Tooltip("空中で目標速度へ近づける加速度（m/s^2）。地上と壁走り中は目標速度へ即座に合わせる")]
        private float _airAcceleration = 25f;

        [Header("ジャンプの軌道（重力の倍率）")]
        [SerializeField, Min(0f)]
        [Tooltip("上昇中の倍率。大きいほど素早く上がり、ボタンを放して跳ぶ低いジャンプが低くなる")]
        private float _riseGravityScale = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("ジャンプボタンの長押し中の、上昇中の倍率。上昇中の倍率より小さくして、高く跳べるようにする")]
        private float _holdRiseGravityScale = 0.8f;

        [SerializeField, Min(0f)]
        [Tooltip("最高点の前後（縦の速さが閾値より小さいとき）の倍率。小さいほどふわっとする")]
        private float _apexGravityScale = 0.6f;

        [SerializeField, Min(0f)]
        [Tooltip("最高点の前後とみなす、縦の速さの閾値（m/s）")]
        private float _apexSpeedThreshold = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("下降中の倍率。落下の速さが増えると、最高点の前後の倍率からここへ近づく")]
        private float _fallGravityScale = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("下降の倍率に達するまでの、落下の速さの幅（m/s）。大きいほど加速度が緩やかに上がる")]
        private float _fallRampSpeed = 12f;

        private IPlayerMovementState _movementState;
        private Func<Vector3, float, Vector3?> _step;
        private IDisposable _lookSubscription;
        private Vector3 _horizontalVelocity;

        /// <summary> 縦の速度の正本。重力は Rigidbody ではなくこの値に足す </summary>
        private float _verticalVelocity;

        /// <summary> 今の空中が、ジャンプや打ち上げで始まったか。足場から歩いて落ちたときは false </summary>
        private bool _isJumpArc;

        /// <summary> ワープを始める直前の水平速度。ワープが終わったときにこの値へ戻す </summary>
        private Vector3 _preWarpHorizontalVelocity;

        /// <summary> 直前の FixedUpdate で反映した移動モード </summary>
        private PlayerMoveMode _lastAppliedMode;

        /// <summary> プレイヤーの当たり判定のカプセルの、ワールド空間の外接箱 </summary>
        public Bounds BodyBounds => _collider.bounds;

        /// <summary> プレイヤーの Rigidbody のワールド位置 </summary>
        public Vector3 Position => _rigidbody != null ? _rigidbody.position : transform.position;

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
            _rigidbody.useGravity = false;
            playerBoard.RegisterSceneState<IPlayerContactState>(_contactState, gameObject.scene.buildIndex);
            _lookSubscription = lookState.RegisterOnLookInputChanged(OnLookInputChanged);

            // 派生の検証を通す為、base ではなく仮想メソッド側を呼ぶ
            Initialize();
        }

        /// <summary>
        /// プレイヤーを指定の位置へ置き、速度を止める。補間を有効にした Rigidbody が前の位置から滑らないよう、Transform と Rigidbody の両方を書く。
        /// </summary>
        /// <param name="position">プレイヤーの Transform を置くワールド位置</param>
        public void PlaceAt(Vector3 position)
        {
            if (_rigidbody == null) return;

            _rigidbody.transform.position = position;
            _rigidbody.position = position;
            _rigidbody.linearVelocity = Vector3.zero;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            Physics.SyncTransforms();
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
        /// 1ステップ分の処理を次の順で行う。
        /// 1. 直前の物理ステップの結果から触れている物を判定し、PlayerContactState に書き込む
        /// 2. 視線の向きと経過時間を渡して PlayerMovementService.Step を呼び、打ち出し速度を受け取る
        /// 3. 水平速度を目標速度（PlayerMovementState.TargetVelocity の水平成分）へ、地上と壁走り中は即座に、空中は加速度を制限して合わせ、
        ///    自前で持つ縦の速度（壁走り中は 0、それ以外は重力を足す）と合わせて Rigidbody に反映する
        /// Step が触れている物を読むので、1は2より先に行う。
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

                _verticalVelocity = 0f;
                _rigidbody.linearVelocity = _movementState.TargetVelocity;
                return;
            }

            if (mode == PlayerMoveMode.Aiming)
            {
                _verticalVelocity = 0f;
                _rigidbody.linearVelocity = Vector3.zero;
                _horizontalVelocity = Vector3.zero;
                return;
            }

            var hasWarpEnded = previousMode == PlayerMoveMode.Warping;
            if (hasWarpEnded)
            {
                _horizontalVelocity = _preWarpHorizontalVelocity;
            }

            var isWallRunning = mode == PlayerMoveMode.WallRunning;

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
            if (isGrounded || isWallRunning)
            {
                _horizontalVelocity = target;
            }
            else if (target.sqrMagnitude > 0f)
            {
                // 入力がないときは今の水平速度を保つ。減速させると壁キックの勢いが空中で消える
                _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, target, _airAcceleration * Time.fixedDeltaTime);
            }

            UpdateVerticalVelocity(launchVelocity, isGrounded, isWallRunning || hasWarpEnded);
            _rigidbody.linearVelocity = new Vector3(_horizontalVelocity.x, _verticalVelocity, _horizontalVelocity.z);
        }

        /// <summary>
        /// 縦の速度を更新する。重力は Rigidbody に任せず自前で足す。
        /// 物理が押し戻しで作った上向きの速度は取り込まず、天井などで止められたとき（Rigidbody の値が自分の値より小さいとき）だけ取り込む。
        /// </summary>
        private void UpdateVerticalVelocity(Vector3? launchVelocity, bool isGrounded, bool resetsToZero)
        {
            if (resetsToZero)
            {
                _verticalVelocity = 0f;
                _isJumpArc = false;
                return;
            }

            _verticalVelocity = Mathf.Min(_verticalVelocity, _rigidbody.linearVelocity.y);
            _verticalVelocity += Physics.gravity.y * GetGravityScale() * Time.fixedDeltaTime;

            if (launchVelocity.HasValue)
            {
                _verticalVelocity = launchVelocity.Value.y;
                _isJumpArc = true;
            }
            else if (isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = 0f;
                _isJumpArc = false;
            }
        }

        /// <summary>
        /// 縦の速度から、最高点の前後・上昇中・下降中のどれかを判定し、重力の倍率を返す。
        /// </summary>
        private float GetGravityScale()
        {
            // ジャンプの軌道の緩急は、ジャンプや打ち上げの空中だけに付ける。歩いて落ちたときは最初から下降中の倍率で落ちる
            if (!_isJumpArc) return _fallGravityScale;

            if (Mathf.Abs(_verticalVelocity) < _apexSpeedThreshold) return _apexGravityScale;
            if (_verticalVelocity < 0f)
            {
                // 落下の速さが増えるにつれて、最高点の前後の倍率から下降中の倍率へ緩やかに近づける
                var ramp = Mathf.Clamp01((-_verticalVelocity - _apexSpeedThreshold) / Mathf.Max(_fallRampSpeed, 0.01f));
                return Mathf.Lerp(_apexGravityScale, _fallGravityScale, ramp);
            }

            return _movementState.IsJumpHolding ? _holdRiseGravityScale : _riseGravityScale;
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

            if (TryFindWallNormal(bottomSphereCenter, topSphereCenter, radius, out var wallNormal))
            {
                contacts |= PlayerContact.Wall;
            }

            _contactState.SetContacts(contacts, wallNormal);

            // ガラスの板はコライダーを持たず、触れたら割れて通り抜けられる。割れの起点はプレイヤーの中心
            GlassPane.ShatterInCapsule(bottomSphereCenter, topSphereCenter, radius + _glassContactDistance, center);
        }

        /// <summary>
        /// カプセルの下側の球を下向きに飛ばし、_groundCheckDistance 以内に地面があるかを判定する。
        /// </summary>
        private bool IsGrounded(Vector3 bottomSphereCenter, float radius)
        {
            // SphereCast は開始時点で重なっているコライダーを検出しない。
            // 落下中に1ステップで地面へめり込んでも取りこぼさないよう、球をカプセルより SKIN_WIDTH だけ小さくし、
            // 下側の球の中心から半径分だけ上を起点にして飛ばす（半径 + SKIN_WIDTH までのめり込みを許容する）
            const float SKIN_WIDTH = 0.05f;
            var castRadius = radius - SKIN_WIDTH;
            var castOrigin = bottomSphereCenter + Vector3.up * radius;
            var castDistance = radius + SKIN_WIDTH + _groundCheckDistance;

            return Physics.SphereCast(castOrigin, castRadius, Vector3.down, out _, castDistance,
                _groundLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// カプセルを水平な 8 方向へ _wallCheckDistance だけ飛ばし、壁に当たったら、最も近い壁の、壁から離れる向きの水平な単位ベクトルを返す。
        /// 壁は、_wallLayers のコライダーか、ボクセルのピース、Brilliant Skill の言葉（BrilliantWord）で、面の向きが床や天井に近いものは除く。
        /// 凸でない MeshCollider（ボクセルのチャンクの当たり判定）は Collider.ClosestPoint で面の位置を求められない為、キャストで調べる。
        /// カプセルは WALL_SKIN_WIDTH だけ細くして飛ばし、壁に少しめり込んでいても見つけられるようにする。
        /// </summary>
        private bool TryFindWallNormal(Vector3 bottomSphereCenter, Vector3 topSphereCenter, float radius,
            out Vector3 wallNormal)
        {
            wallNormal = Vector3.zero;

            var castRadius = radius - WALL_SKIN_WIDTH;
            var castDistance = WALL_SKIN_WIDTH + _wallCheckDistance;
            var castLayers = _wallLayers | _groundLayers;
            var nearestDistance = float.MaxValue;

            for (var i = 0; i < WALL_CAST_DIRECTION_COUNT; i++)
            {
                var direction = Quaternion.Euler(0f, i * 360f / WALL_CAST_DIRECTION_COUNT, 0f) * Vector3.forward;
                var hitCount = Physics.CapsuleCastNonAlloc(bottomSphereCenter, topSphereCenter, castRadius, direction,
                    _wallHitBuffer, castDistance, castLayers, QueryTriggerInteraction.Ignore);

                for (var h = 0; h < hitCount; h++)
                {
                    var hit = _wallHitBuffer[h];
                    if (hit.distance >= nearestDistance || Mathf.Abs(hit.normal.y) > MAX_WALL_NORMAL_Y) continue;
                    if (!IsWall(hit.collider)) continue;

                    // 壁の向きが水平に決まらない面は除く
                    var horizontal = new Vector3(hit.normal.x, 0f, hit.normal.z);
                    if (horizontal.sqrMagnitude < 1e-4f) continue;

                    nearestDistance = hit.distance;
                    wallNormal = horizontal.normalized;
                }
            }

            return wallNormal != Vector3.zero;
        }

        /// <summary>
        /// コライダーが壁走りのできる壁か。_wallLayers のレイヤーか、ボクセルのピース、Brilliant Skill の言葉なら壁。
        /// </summary>
        private bool IsWall(Collider wallCollider)
        {
            if (((1 << wallCollider.gameObject.layer) & _wallLayers.value) != 0) return true;

            return wallCollider.GetComponentInParent<VoxelPiece>() != null ||
                   wallCollider.GetComponentInParent<BrilliantWord>() != null;
        }
    }
}
