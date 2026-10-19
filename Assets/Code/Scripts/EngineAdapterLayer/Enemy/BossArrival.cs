using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスが水晶のあった所へ飛んでくる飛来を扱う。ボスの根に付ける。
    /// ムービー（Timeline の PlayableDirector）があれば再生し、終わったらボスを着く所へ置く。なければ、ボスを放物線で着く所まで飛ばす。
    /// 着く所の高さは、飛ぶ前の地面からの高さを保つ。着いたら、着いた所を中心に縦長のカプセル（側面は円柱）で周りのボクセルを削り、同じ半径の敵を倒して、戦う場所を空ける。あわせて土煙を出し、画面を揺らす。
    /// </summary>
    public sealed class BossArrival : MonoBehaviour
    {
        /// <summary> 削る間だけ保護したボスのピース。削り終えたら保護を外す </summary>
        private readonly List<VoxelPiece> _temporarilyProtected = new();

        [SerializeField]
        [Tooltip("飛来のムービー。なければ放物線で飛ばす")]
        private PlayableDirector _director;

        [SerializeField]
        [Tooltip("地面のレイヤー")]
        private LayerMask _groundLayers = ~0;

        [SerializeField, Min(0.1f)]
        [Tooltip("ムービーがないときに、飛んでから着くまでの時間（秒）")]
        private float _flightTime = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("ムービーがないときの、放物線の頂点の高さ（m）")]
        private float _arcHeight = 40f;

        [SerializeField, Min(0.1f)]
        [Tooltip("地面を探すレイの長さ（m）")]
        private float _groundRayLength = 200f;

        [SerializeField, Min(0f)]
        [Tooltip("着いたときに周りのボクセルを削る円柱と、敵を倒す球の半径（m）。0 なら何もしない")]
        private float _clearRadius = 15f;

        [SerializeField, Min(0f)]
        [Tooltip("着いたときに削る円柱の、地面からの高さ（m）")]
        private float _clearHeight = 80f;

        [SerializeField]
        [Tooltip("着いたときに足元へ土煙を出すパーティクル。ワールド空間でシミュレーションする。未設定なら出さない")]
        private ParticleSystem _landingDust;

        [SerializeField, Min(0)]
        [Tooltip("着いたときに出す土煙の粒の数")]
        private int _landingDustCount = 60;

        [SerializeField, Min(0f)]
        [Tooltip("着いたときに画面を揺らす強さ。0 なら揺らさない")]
        private float _landingShakeForce = 1.5f;

        private CinemachineImpulseSource _impulseSource;

        /// <summary> カプセルで削る関数。引数は両端（ワールド座標）と半径 </summary>
        private Action<Vector3, Vector3, float> _carveCapsule;

        /// <summary> 球の中の敵を倒す関数。引数は中心（ワールド座標）と半径 </summary>
        private Action<Vector3, float> _defeatInRadius;

        /// <summary> 飛ぶ前の位置（ワールド座標） </summary>
        private Vector3 _from;

        /// <summary> 着く所（ワールド座標） </summary>
        private Vector3 _to;

        /// <summary> 飛び始めてからの時間（秒） </summary>
        private float _time;

        /// <summary> 飛んでいるか </summary>
        public bool IsArriving { get; private set; }

        /// <summary>
        /// 着いたときに周りを削る関数と、敵を倒す関数を受け取る。
        /// </summary>
        /// <param name="carveCapsule">カプセルで削る関数。null なら削らない</param>
        /// <param name="defeatInRadius">球の中の敵を倒す関数。null なら倒さない</param>
        public void Initialize(Action<Vector3, Vector3, float> carveCapsule, Action<Vector3, float> defeatInRadius)
        {
            _carveCapsule = carveCapsule;
            _defeatInRadius = defeatInRadius;
        }

        /// <summary>
        /// destination の上へ飛び始める。
        /// </summary>
        /// <param name="destination">着く所の水平の位置（ワールド座標）。高さは地面から決める</param>
        public void Begin(Vector3 destination)
        {
            _from = transform.position;
            _to = destination;
            _to.y = GetGroundHeight(destination) + (_from.y - GetGroundHeight(_from));
            _time = 0f;
            IsArriving = true;

            if (_director != null) _director.Play();
        }

        private void Update()
        {
            if (!IsArriving) return;

            _time += Time.deltaTime;
            if (_director != null)
            {
                if (_director.state == PlayState.Playing && _time < _director.duration) return;

                Finish();
                return;
            }

            var t = Mathf.Clamp01(_time / _flightTime);
            transform.position = Vector3.Lerp(_from, _to, t) + Vector3.up * (4f * _arcHeight * t * (1f - t));
            if (t >= 1f) Finish();
        }

        private void Awake()
        {
            if (_landingShakeForce > 0f) _impulseSource = CreateImpulseSource();
        }

        private void Finish()
        {
            transform.position = _to;
            IsArriving = false;
            ClearAround();
            PlayLandingEffects();
        }

        /// <summary>
        /// 着いた所の足元に土煙を出し、画面を揺らす。
        /// </summary>
        private void PlayLandingEffects()
        {
            if (_landingDust != null && _landingDustCount > 0)
            {
                var ground = _to;
                ground.y = GetGroundHeight(_to);
                var emitParams = new ParticleSystem.EmitParams { position = ground, applyShapeToPosition = true };
                _landingDust.Emit(emitParams, _landingDustCount);
            }

            if (_impulseSource != null) _impulseSource.GenerateImpulseWithForce(_landingShakeForce);
        }

        /// <summary>
        /// 着いたときに画面を揺らす CinemachineImpulseSource を作る。揺れは、カメラの CinemachineImpulseListener が受ける。
        /// </summary>
        private CinemachineImpulseSource CreateImpulseSource()
        {
            var source = gameObject.AddComponent<CinemachineImpulseSource>();
            source.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            source.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            source.ImpulseDefinition.ImpulseDuration = 0.6f;
            source.DefaultVelocity = Vector3.down;
            return source;
        }

        /// <summary>
        /// 着いた所の周りの、同じ半径の球の中の敵を倒し、ボクセルを地面から決めた高さまでの円柱で削る。カプセルの丸い端は、地面の下と円柱の上へ出す。
        /// ボス自身（武装を含む）は削らないよう、削る間だけ保護していないピースを保護する。
        /// </summary>
        private void ClearAround()
        {
            if (_clearRadius <= 0f) return;

            var ground = _to;
            ground.y = GetGroundHeight(_to);
            _defeatInRadius?.Invoke(ground, _clearRadius);
            if (_carveCapsule == null) return;

            foreach (var piece in GetComponentsInChildren<VoxelPiece>())
            {
                if (piece.IsProtected) continue;

                piece.SetProtected(true);
                _temporarilyProtected.Add(piece);
            }

            _carveCapsule(ground, ground + Vector3.up * _clearHeight, _clearRadius);

            foreach (var piece in _temporarilyProtected)
            {
                if (piece != null) piece.SetProtected(false);
            }

            _temporarilyProtected.Clear();
        }

        /// <summary>
        /// 位置 position の真下の地面の高さ。地面がなければ位置の高さ。
        /// </summary>
        private float GetGroundHeight(Vector3 position)
        {
            var origin = position + Vector3.up * (_groundRayLength * 0.5f);
            return BossGroundProbe.TryGetGround(transform, origin, _groundRayLength, _groundLayers, out var ground)
                ? ground.y
                : position.y;
        }
    }
}
