using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 天照塔を、ボクセルの帯（VoxelBuilding）が倒壊したときに、少ないボクセルの側へ倒して橋にする。
    /// 倒れる向きは、帯に残ったボクセルの重心から見て反対の側（ボクセルの少ない側）にする。倒れる先の目印を指定したときは、その向きを使う。
    /// 塔の上の部分は一番上の階の付属物としてメッシュのまま置く。
    /// 倒れ始めたら、倒れたまとまりのコライダーをすべて捨て、全体を包む凸包の MeshCollider 1つと、帯の足場の箱 1つに置き換える。ピースのコライダーが消えるので、倒れた塔はスキルで削れず、数千個の箱が物理の負荷になることもない。
    /// 倒れ方は物理に任せず、まとまりをキネマティックにして、帯の底の縁を軸に一定の角加速度で回す。塔の上の部分の凸包が地面（Ground レイヤー）に届いた角度で止める。
    /// 止めたら、凸包を歩ける橋にして、橋の上に敵を出す。塔の軸に沿って煙を出して画面を揺らし、塔の下になったビルを、軸に沿った短い区間のキューにして時間をかけて削る。
    /// </summary>
    public sealed class TowerTopple : MonoBehaviour
    {
        private const string GROUND_LAYER = "Ground";
        private const string PLAYER_LAYER = "Player";
        private const string ENEMY_LAYER = "Enemy";

        /// <summary> 凸包の範囲と重なる地面のコライダーを一度に受け取る数 </summary>
        private const int GROUND_OVERLAP_CAPACITY = 64;

        /// <summary> 倒れる先に地面がないときの歯止めになる、倒れる角度の上限（度） </summary>
        private const float MAX_TOPPLE_ANGLE = 120f;

        /// <summary> 地面に届いた角度を、直前の角度との間で絞り込む二分探索の回数 </summary>
        private const int CONTACT_REFINE_COUNT = 5;

        /// <summary> 帯の底の縁の角が、同じ縁にあるとみなす距離（m） </summary>
        private const float EDGE_TOLERANCE = 0.01f;

        private readonly Collider[] _groundOverlaps = new Collider[GROUND_OVERLAP_CAPACITY];

        /// <summary> 削る区間のキュー。両端（ワールド座標）と半径 </summary>
        private readonly Queue<(Vector3 Start, Vector3 End, float Radius)> _crushQueue = new();

        [SerializeField]
        [Tooltip("塔の帯を積んだ建物。倒壊を受け取る")]
        private VoxelBuilding _building;

        [SerializeField]
        [Tooltip("倒れる先の目印。建物から見た水平方向がそのまま倒れる向きになる。未設定なら、帯に残ったボクセルの少ない側へ倒す")]
        private Transform _fallTarget;

        [SerializeField, Min(0f)]
        [Tooltip("倒れ始めから地面に届くまで一定で与える角加速度（rad/s²）。回る速さは時間に比例して増える")]
        private float _toppleAngularAcceleration = 0.03f;

        [SerializeField]
        [Tooltip("帯に残ったボクセルから倒れる向きが決まらないとき（階がメッシュのままのとき）に、倒れる先にするギミックの ID")]
        private string _fallbackTargetId = "Escape_Arrival";

        [SerializeField, Min(0.1f)]
        [Tooltip("倒れた塔が止まったあと、塔の下になったビルを削る半径の、根元での値（m）。塔の半幅に合わせる")]
        private float _crushRadiusBase = 20f;

        [SerializeField, Min(0.1f)]
        [Tooltip("倒れた塔の下になったビルを削る半径の、先端での値（m）")]
        private float _crushRadiusTip = 8f;

        [SerializeField, Min(1f)]
        [Tooltip("倒れた塔の下になったビルを、塔の軸に沿って削る区間の長さ（m）。根元から先端の順に、1 回に 1 区間ずつ削る")]
        private float _crushSegmentLength = 20f;

        [SerializeField, Min(0.1f)]
        [Tooltip("倒れた塔の下になったビルを、すべての区間で削り終えるまでの時間（秒）。区間を削る間隔は、この時間を区間の数で割った値")]
        private float _crushDuration = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("区間を削るのにかかった時間に対する、次の区間を削るまでに空ける時間の倍率。建物が密集した場所は 1 回で重くなるので、削った時間の分だけ間隔を伸ばして、重いフレームが続かないようにする")]
        private float _crushRestRatio = 4f;

        [SerializeField]
        [Tooltip("塔が止まったときに、軸に沿って出す煙のパーティクル。ワールド空間でシミュレーションする。未設定なら出さない")]
        private ParticleSystem _collapseSmoke;

        [SerializeField, Min(1f)]
        [Tooltip("塔が止まったときに、煙を出す位置の軸に沿った間隔（m）")]
        private float _smokeSpacing = 40f;

        [SerializeField, Min(0)]
        [Tooltip("塔が止まったときに、煙を出す位置ごとに出す粒の数")]
        private int _smokeCountPerPoint = 20;

        [SerializeField, Min(0)]
        [Tooltip("区間を削るたびに、その区間の中央で出す煙の粒の数")]
        private int _crushSmokeCount = 8;

        [SerializeField, Min(0f)]
        [Tooltip("塔が止まったときに画面を揺らす強さ。0 なら揺らさない")]
        private float _stopShakeForce = 3f;

        private IDisposable _subscription;
        private IDisposable _collapsingSubscription;
        private Rigidbody _toppledBody;
        private Transform _upperRoot;
        private Mesh _upperHullMesh;

        /// <summary> 倒れたまとまりに付けた、塔の上の部分の凸包のコライダー </summary>
        private MeshCollider _hullCollider;

        /// <summary> 帯の足場の範囲。倒れたまとまりの空間 </summary>
        private Bounds _bandBounds;

        private bool _hasBandBounds;
        private Vector3 _toppleAxis;

        /// <summary> 倒れる向き側の、帯の底の縁の中心。回転の軸が通る点 </summary>
        private Vector3 _pivot;

        private Vector3 _startPosition;
        private Quaternion _startRotation;

        /// <summary> 倒れ始めから今までの傾き（度） </summary>
        private float _toppleAngle;

        /// <summary> 今の回る速さ（度/秒） </summary>
        private float _toppleSpeed;

        private int _groundMask;

        /// <summary> 次の区間を削るまでの残り時間（秒） </summary>
        private float _crushTimer;

        /// <summary> 区間を削る間隔（秒） </summary>
        private float _crushInterval;

        private CinemachineImpulseSource _impulseSource;
        private VoxelDestructionAdapter _crushAdapter;
        private bool _hasToppled;
        private bool _isToppling;
        private bool _isStopped;
        private bool _hasReleasedSpawns;

        /// <summary> 倒れて止まった塔のまとまり。倒れている途中と、倒れる前は null。軸は transform.up </summary>
        public Rigidbody SettledBody => _hasReleasedSpawns ? _toppledBody : null;

        /// <summary> 倒れた塔の軸に沿った長さ（m）。軸の原点は SettledBody の位置。上の部分の凸包を作る前は 0 </summary>
        public float AxisLength => _upperHullMesh != null ? _upperHullMesh.bounds.max.y : 0f;

        /// <summary> 塔の帯を積んだ建物 </summary>
        public VoxelBuilding Building => _building;

        /// <summary> 帯が倒壊して、塔が倒れ始めたか </summary>
        public bool HasToppled => _hasToppled;

        private void Awake()
        {
            _subscription = _building.RegisterOnCollapsed(OnCollapsed);
            _collapsingSubscription = _building.RegisterOnCollapsing(OnCollapsing);
        }

        private void Start()
        {
            PrepareUpperHull();
        }

        /// <summary>
        /// 倒れて止まった塔の下を削る Adapter を受け取る。渡さなければ削らない。
        /// </summary>
        /// <param name="crushAdapter">ボクセルを削る Adapter</param>
        public void SetCrushAdapter(VoxelDestructionAdapter crushAdapter)
        {
            _crushAdapter = crushAdapter;
        }

        private void Update()
        {
            // 倒れ終わったら、煙で隠しながら、橋の上に敵を出し、塔の下を削り始める
            if (_isStopped && !_hasReleasedSpawns)
            {
                _hasReleasedSpawns = true;
                PlayStopEffects();
                ReleaseTopFaceSpawns();
                EnqueueCrushSegments();
            }

            CrushNextSegment();
        }

        /// <summary>
        /// 止まった塔の軸に沿って煙を出し、画面を揺らす。
        /// </summary>
        private void PlayStopEffects()
        {
            var axis = _toppledBody.transform;
            for (var distance = 0f; distance < AxisLength; distance += _smokeSpacing)
            {
                EmitSmoke(axis.TransformPoint(Vector3.up * distance), _smokeCountPerPoint);
            }

            if (_stopShakeForce <= 0f) return;

            if (_impulseSource == null) _impulseSource = CreateImpulseSource();
            _impulseSource.GenerateImpulseWithForce(_stopShakeForce);
        }

        private void EmitSmoke(Vector3 position, int count)
        {
            if (_collapseSmoke == null || count <= 0) return;

            var emitParams = new ParticleSystem.EmitParams { position = position, applyShapeToPosition = true };
            _collapseSmoke.Emit(emitParams, count);
        }

        /// <summary>
        /// 画面を揺らす CinemachineImpulseSource を作る。揺れは、カメラの CinemachineImpulseListener が受ける。
        /// </summary>
        private CinemachineImpulseSource CreateImpulseSource()
        {
            var source = gameObject.AddComponent<CinemachineImpulseSource>();
            source.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            source.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            source.ImpulseDefinition.ImpulseDuration = 1.5f;
            source.DefaultVelocity = Vector3.down;
            return source;
        }

        /// <summary>
        /// 止まった塔の軸に沿って、塔の下になったビルを削る区間を、根元から先端の順にキューへ積む。削る半径は根元から先端へ線形に細くする。
        /// 倒れる間は塔がビルと接触しない為、止まったあとに、塔が重なった部分を削って潰す。
        /// </summary>
        private void EnqueueCrushSegments()
        {
            var length = AxisLength;
            var count = Mathf.CeilToInt(length / _crushSegmentLength);
            if (_crushAdapter == null || count <= 0) return;

            var axis = _toppledBody.transform;
            for (var i = 0; i < count; i++)
            {
                var start = i * _crushSegmentLength;
                var end = Mathf.Min(start + _crushSegmentLength, length);
                var radius = Mathf.Lerp(_crushRadiusBase, _crushRadiusTip, (start + end) * 0.5f / length);
                _crushQueue.Enqueue((axis.TransformPoint(Vector3.up * start), axis.TransformPoint(Vector3.up * end), radius));
            }

            _crushInterval = _crushDuration / count;
            _crushTimer = 0f;
        }

        /// <summary>
        /// 区間を削る間隔が経ったら、キューの先頭の区間を 1 つ削り、その中央で煙を出す。
        /// 次までの間隔は、決めた間隔と、削るのにかかった時間の倍率の長いほうにする。
        /// </summary>
        private void CrushNextSegment()
        {
            if (_crushQueue.Count == 0) return;

            _crushTimer -= Time.deltaTime;
            if (_crushTimer > 0f) return;

            var (start, end, radius) = _crushQueue.Dequeue();
            var startTime = Time.realtimeSinceStartupAsDouble;
            _crushAdapter.CarveCapsuleAt(start, end, radius);
            var carveTime = (float)(Time.realtimeSinceStartupAsDouble - startTime);
            EmitSmoke((start + end) * 0.5f, _crushSmokeCount);
            _crushTimer = Mathf.Max(_crushInterval, carveTime * _crushRestRatio);
        }

        /// <summary>
        /// 倒れている間、物理の1ステップごとに一定の角加速度で傾きを進める。次の傾きで凸包の点が地面に届くなら、届く直前の傾きで止める。
        /// </summary>
        private void FixedUpdate()
        {
            if (!_isToppling) return;

            _toppleSpeed += _toppleAngularAcceleration * Mathf.Rad2Deg * Time.fixedDeltaTime;
            var nextAngle = _toppleAngle + _toppleSpeed * Time.fixedDeltaTime;
            var isStopping = false;

            if (nextAngle >= MAX_TOPPLE_ANGLE)
            {
                UsefulLogger.LogWarning($"倒れる先に地面がなかった為、{MAX_TOPPLE_ANGLE} 度で止めます。", this);
                nextAngle = MAX_TOPPLE_ANGLE;
                isStopping = true;
            }
            else if (TouchesGround(nextAngle))
            {
                nextAngle = FindContactAngle(_toppleAngle, nextAngle);
                isStopping = true;
            }

            _toppleAngle = nextAngle;
            ApplyAngle(nextAngle);
            if (!isStopping) return;

            _isToppling = false;
            _isStopped = true;
            UsefulLogger.Log($"天照塔が倒れ終わりました。傾き {nextAngle:F2} 度", this);
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
            _collapsingSubscription?.Dispose();
        }

        /// <summary>
        /// 倒れた階のまとまりに塔の上の部分が含まれていれば、当たり判定を倒れた塔用に置き換える。
        /// 上の部分を持たないまとまり（上の階が先に倒れたあとの下の階）は何もしない。
        /// </summary>
        private void OnCollapsing(Transform group)
        {
            if (_upperRoot != null && _upperRoot.IsChildOf(group)) ReplaceWithHull(group);
        }

        /// <summary>
        /// 倒れた階のまとまりに塔の上の部分が含まれていれば、最初の1回だけ、物理から外して倒れる向きへ回し始める。
        /// </summary>
        private void OnCollapsed(Rigidbody body)
        {
            if (_upperRoot == null || !_upperRoot.IsChildOf(body.transform) || _hasToppled) return;

            _hasToppled = true;
            var direction = _fallTarget != null
                ? _fallTarget.position - _building.transform.position
                : EstimateFallDirection();
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f && StageGimmick.TryFind(_fallbackTargetId, out var target))
            {
                direction = target.transform.position - _building.transform.position;
                direction.y = 0f;
            }

            UsefulLogger.Log($"天照塔が倒れ始めました。倒れる向き {direction.normalized}（水平の長さ {direction.magnitude:F3}）", this);
            if (direction.sqrMagnitude < 1e-6f)
            {
                UsefulLogger.LogError("倒れる向きが決まらない為、塔を物理のまま倒します。", this);
                return;
            }

            direction.Normalize();
            _toppledBody = body;
            _building.DetachBody(body);
            body.isKinematic = true;

            // 水平な倒れる向きに対して、上向きとの外積が倒れる向きに傾ける回転軸になる
            _toppleAxis = Vector3.Cross(Vector3.up, direction);
            _pivot = FindPivot(body.transform, direction);
            _startPosition = body.position;
            _startRotation = body.rotation;
            _groundMask = 1 << LayerMask.NameToLayer(GROUND_LAYER);

            UsefulLogger.Log($"回転の軸が通る点 {_pivot}", this);
            _isToppling = true;
        }

        /// <summary>
        /// 倒れた階のまとまり group の、倒れる向き direction 側の底の縁の中心を返す。縁は帯の足場の範囲の底の面にある。足場の範囲がなければ、まとまりの原点を返す。
        /// </summary>
        private Vector3 FindPivot(Transform group, Vector3 direction)
        {
            if (!_hasBandBounds)
            {
                UsefulLogger.LogWarning("帯の足場の範囲が取れなかった為、まとまりの原点を軸にして倒します。", this);
                return group.position;
            }

            var min = _bandBounds.min;
            var max = _bandBounds.max;
            var corners = new Vector3[4];
            var best = float.NegativeInfinity;
            for (var i = 0; i < corners.Length; i++)
            {
                corners[i] = group.TransformPoint(new Vector3(
                    (i & 1) == 0 ? min.x : max.x, min.y, (i & 2) == 0 ? min.z : max.z));
                best = Mathf.Max(best, Vector3.Dot(corners[i], direction));
            }

            var sum = Vector3.zero;
            var count = 0;
            foreach (var corner in corners)
            {
                if (Vector3.Dot(corner, direction) < best - EDGE_TOLERANCE) continue;

                sum += corner;
                count++;
            }

            return sum / count;
        }

        /// <summary>
        /// まとまりを、倒れ始めの姿勢から軸まわりに angle 度傾けた姿勢にする。
        /// </summary>
        private void ApplyAngle(float angle)
        {
            var rotation = Quaternion.AngleAxis(angle, _toppleAxis);
            _toppledBody.MoveRotation(rotation * _startRotation);
            _toppledBody.MovePosition(_pivot + rotation * (_startPosition - _pivot));
        }

        /// <summary>
        /// 軸まわりに angle 度傾けたとき、塔の上の部分の凸包が地面（Ground レイヤー）にめり込むか。
        /// 凸包の範囲と重なる地面のコライダーだけを、凸包を傾けた姿勢に置いた場合の重なりで確かめる。
        /// 柱の途中が地面の縁に乗る場合があるので、凸包の頂点ではなく凸包そのものを使う。
        /// </summary>
        private bool TouchesGround(float angle)
        {
            var rotation = Quaternion.AngleAxis(angle, _toppleAxis);
            var position = _pivot + rotation * (_startPosition - _pivot);
            rotation *= _startRotation;

            var bounds = _upperHullMesh.bounds;
            var count = Physics.OverlapBoxNonAlloc(position + rotation * bounds.center, bounds.extents, _groundOverlaps, rotation,
                _groundMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var ground = _groundOverlaps[i];
                if (Physics.ComputePenetration(_hullCollider, position, rotation, ground, ground.transform.position,
                        ground.transform.rotation, out _, out _)) return true;
            }

            return false;
        }

        /// <summary>
        /// 届いていない傾き clearAngle と、届いている傾き touchingAngle の間を絞り込み、届く直前の傾きを返す。
        /// </summary>
        private float FindContactAngle(float clearAngle, float touchingAngle)
        {
            for (var i = 0; i < CONTACT_REFINE_COUNT; i++)
            {
                var middle = (clearAngle + touchingAngle) * 0.5f;
                if (TouchesGround(middle)) touchingAngle = middle;
                else clearAngle = middle;
            }

            return clearAngle;
        }

        /// <summary>
        /// 塔の上の部分の凸包を、立っている間に作って焼いておく。倒れる瞬間に作ると、点が多く数百 ms かかる為。
        /// 凸包の点は建物の空間で持つ。倒れたまとまりは建物の原点に置かれるので、そのまま子にできる。
        /// 上の部分の元の MeshCollider は、凸でないと動く Rigidbody に付けられず、付けると重くなる為、無効にする。
        /// </summary>
        private void PrepareUpperHull()
        {
            var face = _building.GetComponentInChildren<TowerFace>(true);
            if (face == null) return;

            _upperRoot = face.transform.parent;
            var points = new List<Vector3>();
            // 塔の上の部分の MeshCollider は、上の部分のメッシュと同じ階の付属物の中にある。階のピースの MeshCollider は VoxelPiece を持つので除く
            foreach (var collider in _building.GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.sharedMesh == null || collider.GetComponent<VoxelPiece>() != null) continue;

                foreach (var vertex in collider.sharedMesh.vertices)
                {
                    points.Add(_building.transform.InverseTransformPoint(collider.transform.TransformPoint(vertex)));
                }

                collider.enabled = false;
            }

            _upperHullMesh = CreateHullMesh(points);
            Physics.BakeMesh(_upperHullMesh.GetInstanceID(), true);
        }

        /// <summary>
        /// 階のまとまり group の子のコライダーをすべて消し、倒れた塔用の当たり判定に置き換える。
        /// 塔の上の部分は凸包の MeshCollider 1つ（プレイヤー・敵・地面と接触）。帯は、全体で足場の箱 1つ（プレイヤー・敵と接触）を付ける。
        /// 地面以外のコライダー（建物のボクセルなど）とは接触しない。
        /// </summary>
        private void ReplaceWithHull(Transform group)
        {
            var hasBounds = false;
            var bandBounds = new Bounds();
            foreach (var collider in group.GetComponentsInChildren<Collider>())
            {
                if (collider is BoxCollider box)
                {
                    // 箱の角をすべて使うと数万点になるので、帯全体の 1 つの範囲（まとまりの空間）にまとめる
                    var half = box.size * 0.5f;
                    for (var i = 0; i < 8; i++)
                    {
                        var corner = box.center + new Vector3(
                            (i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                        var local = group.InverseTransformPoint(box.transform.TransformPoint(corner));
                        if (hasBounds) bandBounds.Encapsulate(local);
                        else bandBounds = new Bounds(local, Vector3.zero);
                        hasBounds = true;
                    }
                }

                // Destroy は遅れて消える為、Rigidbody を付けるまでに無効にして外す
                collider.enabled = false;
                Destroy(collider);
            }

            _bandBounds = bandBounds;
            _hasBandBounds = hasBounds;

            var groundMask = 1 << LayerMask.NameToLayer(GROUND_LAYER);
            var peopleMask = (1 << LayerMask.NameToLayer(PLAYER_LAYER)) | (1 << LayerMask.NameToLayer(ENEMY_LAYER));
            if (hasBounds) AddFootCollider(group, bandBounds, peopleMask);

            var upperObject = new GameObject("TowerHull_Upper") { layer = group.gameObject.layer };
            upperObject.transform.SetParent(group, false);
            var upper = upperObject.AddComponent<MeshCollider>();
            upper.convex = true;
            upper.sharedMesh = _upperHullMesh;
            upper.excludeLayers = ~(groundMask | peopleMask);
            _hullCollider = upper;
        }

        /// <summary>
        /// 帯全体の範囲 bounds に、プレイヤーと敵が乗れる足場の箱を 1 つ付ける。
        /// </summary>
        private void AddFootCollider(Transform group, Bounds bounds, int peopleMask)
        {
            var footObject = new GameObject("TowerFoot") { layer = group.gameObject.layer };
            footObject.transform.SetParent(group, false);

            var box = footObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            box.excludeLayers = ~peopleMask;
        }

        /// <summary>
        /// 凸包の計算は頂点だけを使うので、三角形は点をそのままつなげた形だけ作る。
        /// </summary>
        private static Mesh CreateHullMesh(List<Vector3> points)
        {
            var indices = new int[(points.Count + 2) / 3 * 3];
            for (var i = 0; i < indices.Length; i++)
            {
                indices[i] = math.min(i, points.Count - 1);
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(points);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            return mesh;
        }

        /// <summary>
        /// 塔の上の部分の面のうち、法線が最も上を向いた面を選び、その面の子のスポーン位置の保留と、飛び移りの目印の保留を解く。
        /// </summary>
        private void ReleaseTopFaceSpawns()
        {
            TowerFace top = null;
            var bestUp = float.NegativeInfinity;
            foreach (var face in _upperRoot.GetComponentsInChildren<TowerFace>())
            {
                var up = face.WorldNormal.y;
                if (up <= bestUp) continue;

                bestUp = up;
                top = face;
            }

            if (top == null)
            {
                UsefulLogger.LogWarning("塔の上の部分に TowerFace がない為、橋の上に敵を出せません。", this);
                return;
            }

            var count = 0;
            foreach (var point in top.GetComponentsInChildren<EnemySpawnPoint>(true))
            {
                point.Release();
                count++;
            }

            var anchorCount = 0;
            foreach (var anchor in top.GetComponentsInChildren<FlightAnchor>(true))
            {
                anchor.Release();
                anchorCount++;
            }

            UsefulLogger.Log($"橋の上の面 {top.name} のスポーン位置 {count} 個と、飛び移りの目印 {anchorCount} 個を解放しました。", this);
        }

        /// <summary>
        /// 帯に残ったボクセルの重心（ピースの範囲の中心を体積で重みづけ）から、帯の根元の中心へ向かう向きを返す。ボクセルの少ない側を指す。残りがなければゼロ。
        /// </summary>
        private Vector3 EstimateFallDirection()
        {
            var weighted = Vector3.zero;
            var total = 0f;
            foreach (var floor in _building.Floors)
            {
                foreach (var part in floor.Parts)
                {
                    if (part == null) continue;

                    var volume = part.Volume;
                    weighted += part.WorldBounds.center * volume;
                    total += volume;
                }
            }

            return total > 0f ? _building.transform.position - weighted / total : Vector3.zero;
        }
    }
}
