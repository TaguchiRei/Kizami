using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 天照塔を、ボクセルの帯（VoxelBuilding）が倒壊したときに、少ないボクセルの側へ倒して橋にする。
    /// 倒れる向きは、帯に残ったボクセルの重心から見て反対の側（ボクセルの少ない側）にする。倒れる先の目印を指定したときは、その向きを使う。
    /// 塔の上の部分は一番上の階の付属物としてメッシュのまま置く。
    /// 倒れ始めたら、倒れたまとまりのコライダーをすべて捨て、全体を包む凸包の MeshCollider 1つに置き換える。ピースのコライダーが消えるので、倒れた塔はスキルで削れず、数千個の箱が物理の負荷になることもない。
    /// VoxelBuilding が倒れた階を止めてキネマティックにしたら、その凸包を歩ける橋にして、橋の上に敵を出す。
    /// VoxelBuilding はピースの体積だけで質量を決め、塔の上の部分を数えない為、質量を足す。
    /// 帯が壊れただけでは塔は根元の面に乗ったまま止まるので、倒れる向きへ角速度を与え、傾きが一定の角度に届くまで角加速度を与え続ける。
    /// </summary>
    public sealed class TowerTopple : MonoBehaviour
    {
        /// <summary> 傾けたあとの眠る閾値。巨大な凸包は地面の上でわずかに揺れ続け、既定の閾値では眠らない </summary>
        private const float REST_SLEEP_THRESHOLD = 0.5f;

        [SerializeField]
        [Tooltip("塔の帯を積んだ建物。倒壊を受け取る")]
        private VoxelBuilding _building;

        [SerializeField]
        [Tooltip("倒れる先の目印。建物から見た水平方向がそのまま倒れる向きになる。未設定なら、帯に残ったボクセルの少ない側へ倒す")]
        private Transform _fallTarget;

        [SerializeField, Min(0f)]
        [Tooltip("倒れ始めに与える、倒れる向きの軸まわりの角速度（rad/s）")]
        private float _toppleAngularSpeed = 0.05f;

        [SerializeField, Min(0f)]
        [Tooltip("倒れる向きの軸まわりに、傾きが次の角度に届くまで与え続ける角加速度（rad/s²）。質量によらない。倒れ始めの勢いだけでは、根元の面に乗ったまま眠って止まる為")]
        private float _toppleAngularAcceleration = 0.05f;

        [SerializeField]
        [Tooltip("帯に残ったボクセルから倒れる向きが決まらないとき（階がメッシュのままのとき）に、倒れる先にするギミックの ID")]
        private string _fallbackTargetId = "Escape_Arrival";

        [SerializeField, Range(1f, 90f)]
        [Tooltip("傾きがこの角度（度）に届くまで、角加速度を与え続ける。届いたあとは重力に任せる")]
        private float _driveUntilAngle = 20f;

        [SerializeField, Min(0.1f)]
        [Tooltip("倒れた塔が止まったあと、塔の下になったビルを削る半径の、根元での値（m）。塔の半幅に合わせる")]
        private float _crushRadiusBase = 20f;

        [SerializeField, Min(0.1f)]
        [Tooltip("倒れた塔の下になったビルを削る半径の、先端での値（m）")]
        private float _crushRadiusTip = 8f;

        [SerializeField, Min(1f)]
        [Tooltip("倒れた塔の下になったビルを、塔の軸に沿って削る区間の長さ（m）。区間ごとに 1 フレームずつ削る")]
        private float _crushSegmentLength = 40f;

        [SerializeField, Min(0f)]
        [Tooltip("塔の上の部分の質量（kg）。倒れた階のまとまりの質量に足す")]
        private float _upperMass = 1000000f;

        [SerializeField, Min(0f)]
        [Tooltip("倒れたまとまりの質量の下限（kg）。階をボクセルにせずに倒すと、階の体積が質量に入らない為")]
        private float _minimumMass = 25000000f;

        private const string GROUND_LAYER = "Ground";
        private const string PLAYER_LAYER = "Player";
        private const string ENEMY_LAYER = "Enemy";

        /// <summary> 止まったとみなす速さ（m/s） </summary>
        private const float SETTLED_SPEED = 0.05f;

        /// <summary> 止まったとみなす角速度（rad/s） </summary>
        private const float SETTLED_ANGULAR_SPEED = 0.005f;

        /// <summary> 止まったとみなす速さが続く時間（秒） </summary>
        private const float SETTLED_DURATION = 2f;

        private IDisposable _subscription;
        private IDisposable _collapsingSubscription;
        private Rigidbody _drivenBody;
        private Vector3 _toppleAxis;
        private Rigidbody _toppledBody;
        private Transform _upperRoot;
        private Mesh _upperHullMesh;
        private float _settledTime;
        private int _crushIndex = -1;
        private VoxelDestructionAdapter _crushAdapter;
        private bool _hasToppled;
        private bool _hasReleasedSpawns;

        /// <summary> 倒れて止まった塔のまとまり。倒れている途中と、倒れる前は null。軸は transform.up </summary>
        public Rigidbody SettledBody => _hasReleasedSpawns ? _toppledBody : null;

        /// <summary> 倒れた塔の軸に沿った長さ（m）。軸の原点は SettledBody の位置。上の部分の凸包を作る前は 0 </summary>
        public float AxisLength => _upperHullMesh != null ? _upperHullMesh.bounds.max.y : 0f;

        /// <summary> 塔の帯を積んだ建物 </summary>
        public VoxelBuilding Building => _building;

        /// <summary> 帯が倒壊して、塔が倒れ始めたか </summary>
        public bool HasToppled => _hasToppled;

        /// <summary>
        /// 倒れた塔の上の部分の根元（帯とつながっていた端の中心）のワールド位置を返す。倒れたまとまりと上の部分の凸包が揃うまでは false。
        /// </summary>
        public bool TryGetUpperBase(out Vector3 position)
        {
            var isReady = _toppledBody != null && _upperHullMesh != null;
            var bounds = isReady ? _upperHullMesh.bounds : default;
            position = isReady
                ? _toppledBody.transform.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z))
                : default;
            return isReady;
        }

        private void Awake()
        {
            _subscription = _building.RegisterOnCollapsed(OnCollapsed);
            _collapsingSubscription = _building.RegisterOnCollapsing(OnCollapsing);
        }

        private void Start()
        {
            PrepareUpperHull();
        }

        private void Update()
        {
            StopWhenSettled();

            // 倒れて止まったら（VoxelBuilding がキネマティックにしたら）、橋の上に敵を出す
            if (_toppledBody != null && !_hasReleasedSpawns && _toppledBody.isKinematic)
            {
                _hasReleasedSpawns = true;
                ReleaseTopFaceSpawns();
                _crushAdapter = FindFirstObjectByType<VoxelDestructionAdapter>();
                if (_crushAdapter != null) _crushIndex = 0;
            }

            CrushNextSegment();
        }

        /// <summary>
        /// 止まった塔の軸に沿って、塔の下になったビルを区間ごとに 1 フレームずつ削る。削る半径は根元から先端へ線形に細くする。
        /// 倒れる間は塔がビルと接触しない為、止まったあとに、塔が重なった部分を削って潰す。
        /// </summary>
        private void CrushNextSegment()
        {
            if (_crushIndex < 0) return;

            var length = _upperHullMesh.bounds.max.y;
            var start = _crushIndex * _crushSegmentLength;
            if (start >= length || _crushAdapter == null || _toppledBody == null)
            {
                _crushIndex = -1;
                return;
            }

            var end = Mathf.Min(start + _crushSegmentLength, length);
            var radius = Mathf.Lerp(_crushRadiusBase, _crushRadiusTip, (start + end) * 0.5f / length);
            var axis = _toppledBody.transform;
            _crushAdapter.CarveCapsuleAt(axis.TransformPoint(Vector3.up * start), axis.TransformPoint(Vector3.up * end), radius);
            _crushIndex++;
        }

        /// <summary>
        /// 倒れた塔が、傾けるのをやめたあとに一定時間ほぼ動かなければ、眠らせる。巨大な凸包は物理の眠る判定を満たさず、止まったことが VoxelBuilding に伝わらない為。
        /// </summary>
        private void StopWhenSettled()
        {
            if (_toppledBody == null || _drivenBody != null || _toppledBody.isKinematic || _toppledBody.IsSleeping()) return;

            var isSlow = _toppledBody.linearVelocity.sqrMagnitude < SETTLED_SPEED * SETTLED_SPEED &&
                         _toppledBody.angularVelocity.sqrMagnitude < SETTLED_ANGULAR_SPEED * SETTLED_ANGULAR_SPEED;
            _settledTime = isSlow ? _settledTime + Time.deltaTime : 0f;
            if (_settledTime >= SETTLED_DURATION) _toppledBody.Sleep();
        }

        private void FixedUpdate()
        {
            if (_drivenBody == null) return;

            if (Vector3.Angle(_drivenBody.transform.up, Vector3.up) >= _driveUntilAngle)
            {
                // 以降は重力に任せ、止まったら VoxelBuilding がキネマティックにする
                _drivenBody.sleepThreshold = REST_SLEEP_THRESHOLD;
                _drivenBody = null;
                return;
            }

            _drivenBody.AddTorque(_toppleAxis * _toppleAngularAcceleration, ForceMode.Acceleration);
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
            _collapsingSubscription?.Dispose();
        }

        /// <summary>
        /// 倒れた階のまとまりに塔の上の部分が含まれていれば、質量を足し、最初の1回だけ倒れる向きへ回す。
        /// 上の部分を持たないまとまり（上の階が先に倒れたあとの下の階）は何もしない。
        /// </summary>
        private void OnCollapsing(Transform group)
        {
            if (_upperRoot != null && _upperRoot.IsChildOf(group)) ReplaceWithHull(group);
        }

        private void OnCollapsed(Rigidbody body)
        {
            if (_upperRoot == null || !_upperRoot.IsChildOf(body.transform)) return;

            _toppledBody = body;
            body.mass = Mathf.Max(body.mass + _upperMass, _minimumMass);

            if (_hasToppled) return;

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
            if (direction.sqrMagnitude < 1e-6f) return;

            // 水平な倒れる向きに対して、上向きとの外積が倒れる向きに傾ける回転軸になる
            _toppleAxis = Vector3.Cross(Vector3.up, direction.normalized);
            body.angularVelocity = _toppleAxis * _toppleAngularSpeed;
            _drivenBody = body;
            body.sleepThreshold = 0f;
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
        /// 塔の上の部分は凸包の MeshCollider 1つ（プレイヤー・敵・地面と接触）。
        /// 帯は、全体で足場の箱 1 つ（プレイヤー・敵と接触）と、箱の中心に置いた大きな球 1 つ（地面とだけ接触）を付ける。
        /// 地面以外のコライダー（建物のボクセルなど）とは接触せず、倒れる向きが乱れない。
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

            var groundMask = 1 << LayerMask.NameToLayer(GROUND_LAYER);
            var peopleMask = (1 << LayerMask.NameToLayer(PLAYER_LAYER)) | (1 << LayerMask.NameToLayer(ENEMY_LAYER));
            if (hasBounds) AddFootColliders(group, bandBounds, groundMask, peopleMask);

            var upperObject = new GameObject("TowerHull_Upper") { layer = group.gameObject.layer };
            upperObject.transform.SetParent(group, false);
            var upper = upperObject.AddComponent<MeshCollider>();
            upper.convex = true;
            upper.sharedMesh = _upperHullMesh;
            upper.excludeLayers = ~(groundMask | peopleMask);
        }

        /// <summary>
        /// 帯全体の範囲 bounds に、足場の箱と、地面と接する大きな球を 1 つ付ける。球は箱の中心に置き、箱に収まる最大の半径にする。
        /// </summary>
        private void AddFootColliders(Transform group, Bounds bounds, int groundMask, int peopleMask)
        {
            var footObject = new GameObject("TowerFoot") { layer = group.gameObject.layer };
            footObject.transform.SetParent(group, false);

            var box = footObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            box.excludeLayers = ~peopleMask;

            var sphere = footObject.AddComponent<SphereCollider>();
            sphere.radius = Mathf.Min(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            sphere.center = bounds.center;
            sphere.excludeLayers = ~groundMask;
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
        /// 塔の上の部分の面のうち、法線が最も上を向いた面を選び、その面の子のスポーン位置の保留を解く。
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

            UsefulLogger.Log($"橋の上の面 {top.name} のスポーン位置 {count} 個を解放しました。", this);
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
