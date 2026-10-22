using System;
using System.Collections.Generic;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 体力制の破壊対象。VoxelModelLoader と同じ GameObject に置く。
    /// 読み込んだパーツを保護して、体力が尽きるまで削りも切りも受けない。体力は、通常攻撃・スキルの当たり（攻撃の Adapter が報告する）と、落ちてくる塊の当たり（FixedUpdate で調べる）で減る。
    /// 体力が尽きたら保護を外し、通常攻撃で切り分けられる印を付ける。
    /// </summary>
    [RequireComponent(typeof(VoxelModelLoader))]
    public sealed class HealthTarget : MonoBehaviour
    {
        /// <summary> 落ちる塊を調べるコライダーの最初のバッファの数 </summary>
        private const int INITIAL_HIT_COUNT = 256;

        /// <summary> 落ちる塊を調べるコライダーの数の上限。1つのピースが数千個の箱を持つので、足りなければここまで増やす </summary>
        private const int MAX_HIT_COUNT = 16384;

        /// <summary> 体力を減らした落ちる塊。同じ塊を何度も数えない為に覚える </summary>
        private readonly HashSet<VoxelPiece> _countedPieces = new();

        [SerializeField]
        [Tooltip("ゲージに出す名前")]
        private string _displayName = "Target";

        [SerializeField, Min(1f)]
        [Tooltip("最大体力")]
        private float _maxHealth = 100f;

        [SerializeField, Min(0f)]
        [Tooltip("通常攻撃1回（1回の振り）で減る体力")]
        private float _meleeDamage = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("スキル1回（1回の発動）で減る体力")]
        private float _skillDamage = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("落ちてくる塊の体積1m³あたりで減る体力")]
        private float _collapseDamagePerCubicMeter = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("体力を減らす落ちる塊の、下向きの速さの下限（m/s）。敵の崩落の判定と同じ値にする")]
        private float _minFallSpeed = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("体力を減らす落ちる塊の、体積の下限（m³）。敵の崩落の判定と同じ値にする")]
        private float _minCollapseVolume = 0.5f;

        [SerializeField]
        [Tooltip("落ちる塊を探すレイヤー")]
        private LayerMask _collapseLayers = ~0;

        [SerializeField]
        [Tooltip("体力が尽きたら保護を外し、通常攻撃で切り分けられるようにするか。外さない対象は、体力が尽きても削れも切れもしない")]
        private bool _releasesOnDepleted = true;

        private Collider[] _hitBuffer = new Collider[INITIAL_HIT_COUNT];
        private VoxelModelLoader _loader;
        private Func<float, bool> _applyDamage;
        private Action _onHitWhileDepleted;
        private bool _isProtected;
        private bool _isDepleted;
        private bool _countsCollapse = true;
        private bool _isInvulnerable;

        /// <summary> ゲージに出す名前 </summary>
        public string DisplayName => _displayName;

        /// <summary> 最大体力 </summary>
        public float MaxHealth => _maxHealth;

        /// <summary>
        /// 体力を減らす処理を受け取り、動き始める。読み込みが終わったら保護を始める。
        /// </summary>
        /// <param name="applyDamage">体力を減らす量を受け取り、この呼び出しで体力が尽きたら true を返す処理</param>
        public void Initialize(Func<float, bool> applyDamage)
        {
            _loader = GetComponent<VoxelModelLoader>();
            _applyDamage = applyDamage;
            enabled = true;
        }

        /// <summary>
        /// 通常攻撃が当たったことを報告する。1回の振りにつき1回だけ呼ぶ。
        /// </summary>
        public void ReportMeleeHit()
        {
            ApplyDamage(_meleeDamage);
        }

        /// <summary>
        /// スキルが当たったことを報告する。1回の発動につき1回だけ呼ぶ。
        /// </summary>
        public void ReportSkillHit()
        {
            ApplyDamage(_skillDamage);
        }

        /// <summary>
        /// 量を決めて体力を減らす。当たっている間、続けて減らす攻撃（羽天轟号）が毎フレーム呼ぶ。
        /// </summary>
        /// <param name="amount">減らす体力</param>
        public void ReportDamage(float amount)
        {
            ApplyDamage(amount);
        }

        /// <summary>
        /// 体力が尽きたあとに、通常攻撃かスキルが当たったときに呼ぶ処理を受け取る。体力の配線とは別の Initializer から渡せるよう、Initialize と分ける。
        /// </summary>
        public void SetOnHitWhileDepleted(Action onHitWhileDepleted)
        {
            _onHitWhileDepleted = onHitWhileDepleted;
        }

        /// <summary>
        /// 落ちてくる塊のダメージを数えるかを切り替える。数えない間に触れた塊は、あとからも数えない。
        /// ボスの飛来のように、体力に関わらない崩落が起きる間に切る。
        /// </summary>
        /// <param name="countsCollapse">落ちてくる塊のダメージを数えるか</param>
        public void SetCollapseCounting(bool countsCollapse)
        {
            _countsCollapse = countsCollapse;
        }

        /// <summary>
        /// 無敵を切り替える。無敵の間は、通常攻撃・スキル・落ちてくる塊・量を決めたダメージのどれでも体力が減らない。
        /// </summary>
        /// <param name="isInvulnerable">無敵か</param>
        public void SetInvulnerable(bool isInvulnerable)
        {
            _isInvulnerable = isInvulnerable;
        }

        /// <summary>
        /// 体力が尽きた状態を戻し、また体力を減らせるようにする。保護を外していれば、保護し直す。体力そのものは呼び出し側が戻す。
        /// </summary>
        public void ResetDepleted()
        {
            if (_isDepleted && _releasesOnDepleted)
            {
                foreach (var part in _loader.Parts)
                {
                    part.SetProtected(true);
                    part.SetCutByMelee(false);
                }
            }

            _isDepleted = false;
            _countedPieces.Clear();
        }

        private void Awake()
        {
            enabled = false;
        }

        private void Update()
        {
            if (_isProtected || !_loader.IsLoaded) return;

            foreach (var part in _loader.Parts)
            {
                part.SetProtected(true);
            }

            _isProtected = true;
        }

        private void FixedUpdate()
        {
            if (!_isProtected || _isDepleted) return;

            var hitCount = QueryBox(GetPartsBounds());
            var damage = 0f;
            for (var i = 0; i < hitCount; i++)
            {
                var body = _hitBuffer[i].attachedRigidbody;
                if (body == null || body.linearVelocity.y > -_minFallSpeed) continue;

                var piece = _hitBuffer[i].GetComponentInParent<VoxelPiece>();
                if (piece == null || piece.Volume < _minCollapseVolume || IsOwnPiece(piece)) continue;

                // 同じ塊は何度触れても1回だけ数える。数えない間に触れた塊も、ダメージにはせず数えたことにする
                if (_countedPieces.Add(piece) && _countsCollapse) damage += piece.Volume * _collapseDamagePerCubicMeter;
            }

            ApplyDamage(damage);
        }

        /// <summary>
        /// 体力を減らす。尽きたら保護を解く。
        /// </summary>
        private void ApplyDamage(float amount)
        {
            if (!_isProtected || amount <= 0f || _isInvulnerable) return;

            if (_isDepleted)
            {
                _onHitWhileDepleted?.Invoke();
                return;
            }

            if (!_applyDamage(amount)) return;

            _isDepleted = true;
            if (!_releasesOnDepleted) return;

            foreach (var part in _loader.Parts)
            {
                part.SetProtected(false);
                part.SetCutByMelee(true);
            }

            UsefulLogger.Log($"{_displayName} の体力が尽きた為、保護を外しました。", this);
        }

        /// <summary>
        /// この対象のパーツか、パーツから切り離されたピースか。
        /// </summary>
        private bool IsOwnPiece(VoxelPiece piece)
        {
            return piece.GetComponentInParent<VoxelModelLoader>() == _loader;
        }

        /// <summary>
        /// パーツすべてを含む範囲を返す。
        /// </summary>
        private Bounds GetPartsBounds()
        {
            var parts = _loader.Parts;
            var bounds = parts[0].WorldBounds;
            for (var i = 1; i < parts.Count; i++)
            {
                bounds.Encapsulate(parts[i].WorldBounds);
            }

            return bounds;
        }

        /// <summary>
        /// 範囲のコライダーを _hitBuffer に集める。バッファが埋まったら、上限まで倍にして集め直す。
        /// 対象自身の箱で埋まり、落ちてくる塊を見落とすことを防ぐ為。
        /// </summary>
        private int QueryBox(Bounds bounds)
        {
            while (true)
            {
                var count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, _hitBuffer, Quaternion.identity,
                    _collapseLayers, QueryTriggerInteraction.Ignore);
                if (count < _hitBuffer.Length || _hitBuffer.Length >= MAX_HIT_COUNT) return count;

                Array.Resize(ref _hitBuffer, _hitBuffer.Length * 2);
            }
        }
    }
}
