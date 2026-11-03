using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using Kizami.EngineAdapter.Voxel;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 土遁・造山で、カメラの正面から飛ばしたレイが当たった面に、法線の向きへ伸びる石柱を出す Adapter。
    /// 石柱は 1 つのピースで、根元から先端へ数回に分けてカプセルを足して伸ばす。伸びた範囲にいる敵は倒し、重なったプレイヤーは生えた向きへ弾く。
    /// 弾いた石柱は、プレイヤーが離れるまでコライダーを無効にして、プレイヤーが引っかからないようにする。
    /// ピースは起動時にプールへ用意し、同時に置ける数を超えたら最も古いものを使い回す。
    /// </summary>
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    public sealed class VoxelMoundAdapter : MonoBehaviour
    {
        /// <summary> VoxelPiece より後に LateUpdate を実行する為の実行順 </summary>
        private const int EXECUTION_ORDER = 1000;

        /// <summary> ピースの範囲を、石柱の周りに余らせる幅（m） </summary>
        private const float BOX_MARGIN = 1f;

        [SerializeField]
        [Tooltip("石柱のピースのプレハブ。品質とマテリアルを設定し、通常攻撃で切れる印を付けておく")]
        private VoxelPiece _piecePrefab;

        [SerializeField, Min(1)]
        [Tooltip("同時に置ける数。超えたら最も古いものを使い回す")]
        private int _maxCount = 4;

        [SerializeField, Min(1f)]
        [Tooltip("レイが地形に当たる最大の距離（m）")]
        private float _maxDistance = 30f;

        [SerializeField]
        [Tooltip("レイが当たる対象のレイヤー")]
        private LayerMask _targetLayers = ~0;

        [SerializeField, Min(1)]
        [Tooltip("石柱を伸ばすときに、カプセルを足す回数")]
        private int _growSteps = 6;

        [SerializeField, Min(0f)]
        [Tooltip("石柱の根元を、当たった面の内側へ埋める深さ（m）")]
        private float _embedDepth = 0.5f;

        private readonly List<VoxelPiece> _freePieces = new();
        private readonly List<VoxelPiece> _activePieces = new();
        private readonly HashSet<VoxelPiece> _watchedPieces = new();
        private readonly List<Growth> _growths = new();
        private readonly List<Collider> _colliderBuffer = new();
        private Action<VoxelPiece> _watchPiece;
        private Action<Bounds> _refreshRegion;
        private Action<Vector3, float> _defeatInRadius;
        private Action<Vector3> _requestKnockback;
        private Func<Bounds> _getBodyBounds;
        private IDisposable _pillarSubscription;

        /// <summary>
        /// 線分 from-to 上で、点 point に最も近い点を返す。
        /// </summary>
        private static Vector3 ClosestPointOnSegment(Vector3 from, Vector3 to, Vector3 point)
        {
            var segment = to - from;
            var sqrLength = segment.sqrMagnitude;
            if (sqrLength <= 0f) return from;

            return from + segment * Mathf.Clamp01(Vector3.Dot(point - from, segment) / sqrLength);
        }

        /// <summary>
        /// 石柱の出来事を購読する。IPlayerSkillEffectEvents の登録より後に呼ぶこと。
        /// </summary>
        /// <param name="blackBoard">スキルの効果の出来事の取得元</param>
        /// <param name="watchPiece">石柱のピースを、敵の格子と崩落の判定に見張らせる関数。ピースごとに最初に使うときに1回だけ呼ぶ</param>
        /// <param name="refreshRegion">ピースを使い回して消した範囲を、敵の格子に調べ直させる関数</param>
        /// <param name="defeatInRadius">球の中の敵を倒す関数（EnemySpawnAdapter.DefeatInRadius）。引数は中心と半径</param>
        /// <param name="requestKnockback">プレイヤーを弾く関数（PlayerMovementService.RequestKnockback）。引数は打ち出し速度</param>
        /// <param name="getBodyBounds">プレイヤーの体の範囲（ワールド空間）を返す関数</param>
        public void Initialize(IBlackBoard blackBoard, Action<VoxelPiece> watchPiece, Action<Bounds> refreshRegion,
            Action<Vector3, float> defeatInRadius, Action<Vector3> requestKnockback, Func<Bounds> getBodyBounds)
        {
            _watchPiece = watchPiece;
            _refreshRegion = refreshRegion;
            _defeatInRadius = defeatInRadius;
            _requestKnockback = requestKnockback;
            _getBodyBounds = getBodyBounds;

            if (_piecePrefab == null)
            {
                UsefulLogger.LogError("石柱のピースのプレハブが設定されていない為、石柱を出せません。", this);
                return;
            }

            for (var i = 0; i < _maxCount; i++)
            {
                var piece = Instantiate(_piecePrefab, transform);
                piece.gameObject.SetActive(false);
                _freePieces.Add(piece);
            }

            if (blackBoard.TryGetSceneEvent<PlayerEventBoard, IPlayerSkillEffectEvents>(out var effectEvents, this))
            {
                _pillarSubscription = effectEvents.OnPillar.Register(Raise);
            }
        }

        /// <summary>
        /// カメラの正面に、石柱を出せる面があるか。
        /// </summary>
        public bool CanRaise()
        {
            return _freePieces.Count + _activePieces.Count > 0 && TryRaycast(out _);
        }

        private void Update()
        {
            for (var i = _growths.Count - 1; i >= 0; i--)
            {
                var growth = _growths[i];
                growth.Elapsed += Time.deltaTime;

                var targetStep = Mathf.Min(_growSteps, 1 + Mathf.FloorToInt(growth.Elapsed / growth.Duration * _growSteps));
                while (growth.Step < targetStep)
                {
                    growth.Step++;
                    GrowTo(growth);
                }
            }
        }

        /// <summary>
        /// ピースの再メッシュ化の反映（VoxelPiece.LateUpdate）で有効に戻されたコライダーを、物理の前に無効にし直す為、ピースより後に実行する。
        /// 伸び終えて、透過も終えた石柱は、ここで管理から外す。
        /// </summary>
        private void LateUpdate()
        {
            for (var i = _growths.Count - 1; i >= 0; i--)
            {
                var growth = _growths[i];
                if (growth.IsPassthrough) UpdatePassthrough(growth);
                if (growth.Step >= _growSteps && !growth.IsPassthrough) _growths.RemoveAt(i);
            }
        }

        private void OnDestroy()
        {
            _pillarSubscription?.Dispose();
        }

        /// <summary>
        /// レイが当たった面から法線の向きへ、石柱を伸ばし始める。最初の 1 回分はすぐに足す。
        /// </summary>
        private void Raise(PillarEffectNotice notice)
        {
            if (!TryRaycast(out var hit)) return;

            var growth = new Growth
            {
                Origin = hit.point,
                Direction = hit.normal,
                Radius = notice.Radius,
                Length = notice.Length,
                Duration = Mathf.Max(notice.GrowDuration, 0.01f),
                KnockbackSpeed = notice.KnockbackSpeed
            };
            growth.Piece = TakePiece(growth);
            growth.BaseCenter = growth.Origin - growth.Direction * _embedDepth;
            growth.PreviousTip = growth.BaseCenter;
            growth.Step = 1;
            GrowTo(growth);

            if (growth.Step < _growSteps || growth.IsPassthrough) _growths.Add(growth);
        }

        private bool TryRaycast(out RaycastHit hit)
        {
            hit = default;
            var cameraMain = Camera.main;
            if (cameraMain == null) return false;

            var cameraTransform = cameraMain.transform;
            return Physics.Raycast(cameraTransform.position, cameraTransform.forward, out hit, _maxDistance,
                _targetLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 石柱が収まる箱のピースを、空のボリュームで用意する。向きは法線に合わせる。空きがなければ最も古いものを使い回す。
        /// </summary>
        private VoxelPiece TakePiece(Growth growth)
        {
            VoxelPiece piece;
            if (_freePieces.Count > 0)
            {
                piece = _freePieces[^1];
                _freePieces.RemoveAt(_freePieces.Count - 1);
            }
            else
            {
                piece = _activePieces[0];
                _activePieces.RemoveAt(0);
                _growths.RemoveAll(other => other.Piece == piece);
                _refreshRegion?.Invoke(piece.WorldBounds);
                piece.gameObject.SetActive(false);
            }

            if (_watchedPieces.Add(piece)) _watchPiece?.Invoke(piece);

            var bottom = growth.Origin - growth.Direction * (_embedDepth + growth.Radius + BOX_MARGIN);
            var top = growth.Origin + growth.Direction * (growth.Length + BOX_MARGIN);
            var halfWidth = growth.Radius + BOX_MARGIN;
            var halfHeight = (top - bottom).magnitude * 0.5f;

            piece.transform.SetPositionAndRotation((bottom + top) * 0.5f,
                Quaternion.FromToRotation(Vector3.up, growth.Direction));
            piece.transform.localScale = Vector3.one;
            piece.gameObject.SetActive(true);
            piece.CreateVolume(VoxelBounds.FromCenterExtents(float3.zero, new float3(halfWidth, halfHeight, halfWidth)));

            _activePieces.Add(piece);
            return piece;
        }

        /// <summary>
        /// 石柱を今の段階の長さまで伸ばす。足したカプセルの範囲にいる敵を倒し、重なったプレイヤーを 1 本につき 1 回だけ弾く。
        /// 弾いた石柱は、プレイヤーが離れるまでコライダーを無効にする（UpdatePassthrough）。
        /// 先端の球の中心は、先端の面が長さに届く所に置く。
        /// </summary>
        private void GrowTo(Growth growth)
        {
            var ratio = (float)growth.Step / _growSteps;
            var distance = Mathf.Max(growth.Length * ratio - growth.Radius, -_embedDepth);
            var tip = growth.Origin + growth.Direction * distance;

            if (!growth.IsKnocked && OverlapsPlayer(growth.PreviousTip, tip, growth.Radius))
            {
                growth.IsKnocked = true;
                growth.IsPassthrough = true;
                _requestKnockback?.Invoke(growth.Direction * growth.KnockbackSpeed);
            }

            growth.Piece.ApplyEdit(new CapsuleShape(growth.BaseCenter, tip, growth.Radius), VoxelCsgOperation.Union,
                Space.World);
            if (growth.IsPassthrough) DisableColliders(growth);
            DefeatAlong(growth.PreviousTip, tip, growth.Radius);

            growth.PreviousTip = tip;
        }

        /// <summary>
        /// プレイヤーが石柱に重なっている間は、ピース配下のコライダーを無効にし続ける。ピースの再メッシュ化でコライダーが有効に戻されるので、毎フレーム無効にし直す。
        /// 重なりが解けたら、無効にしたコライダーを有効に戻す。
        /// </summary>
        private void UpdatePassthrough(Growth growth)
        {
            if (OverlapsPlayer(growth.BaseCenter, growth.PreviousTip, growth.Radius))
            {
                DisableColliders(growth);
                return;
            }

            foreach (var collider in growth.DisabledColliders)
            {
                if (collider != null) collider.enabled = true;
            }

            growth.DisabledColliders.Clear();
            growth.IsPassthrough = false;
        }

        /// <summary>
        /// ピース配下の有効なコライダーを無効にし、戻せるように記録する。
        /// </summary>
        private void DisableColliders(Growth growth)
        {
            growth.Piece.GetComponentsInChildren(false, _colliderBuffer);
            foreach (var collider in _colliderBuffer)
            {
                if (!collider.enabled) continue;

                collider.enabled = false;
                growth.DisabledColliders.Add(collider);
            }
        }

        /// <summary>
        /// from から to までに、半径 radius の球を並べて、中の敵を倒す。
        /// </summary>
        private void DefeatAlong(Vector3 from, Vector3 to, float radius)
        {
            if (_defeatInRadius == null) return;

            var count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / radius));
            for (var i = 0; i <= count; i++)
            {
                _defeatInRadius(Vector3.Lerp(from, to, (float)i / count), radius);
            }
        }

        /// <summary>
        /// プレイヤーの体の範囲が、線分 from-to を中心にした半径 radius のカプセルに重なるか。体の範囲を radius だけ広げた箱に、線分上の最も近い点が入るかで見る。
        /// </summary>
        private bool OverlapsPlayer(Vector3 from, Vector3 to, float radius)
        {
            if (_getBodyBounds == null) return false;

            var bounds = _getBodyBounds();
            bounds.Expand(radius * 2f);
            return bounds.Contains(ClosestPointOnSegment(from, to, bounds.center));
        }

        /// <summary>
        /// 伸びている途中の石柱 1 本。
        /// </summary>
        private sealed class Growth
        {
            public VoxelPiece Piece;

            /// <summary> レイが当たった点 </summary>
            public Vector3 Origin;

            /// <summary> 当たった面の法線。石柱が伸びる向き </summary>
            public Vector3 Direction;

            public float Radius;
            public float Length;
            public float Duration;
            public float KnockbackSpeed;
            public float Elapsed;

            /// <summary> カプセルを足し終えた回数 </summary>
            public int Step;

            /// <summary> カプセルの根元の球の中心。面の内側へ埋めた位置 </summary>
            public Vector3 BaseCenter;

            /// <summary> 前の段階で足したカプセルの先端の球の中心 </summary>
            public Vector3 PreviousTip;

            /// <summary> プレイヤーを弾いたか </summary>
            public bool IsKnocked;

            /// <summary> プレイヤーが離れるまで、コライダーを無効にしている間か </summary>
            public bool IsPassthrough;

            /// <summary> 透過のために無効にしたコライダー。戻すときに有効にする </summary>
            public readonly List<Collider> DisabledColliders = new();
        }
    }
}
