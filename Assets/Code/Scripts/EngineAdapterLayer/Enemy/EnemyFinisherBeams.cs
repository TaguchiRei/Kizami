using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 吸収を終えたフィニッシャーが、その場で撃つ極太のビームを扱う。EnemyFinisherAttack と同じ GameObject に置き、EnemyFinisherAttack が初期化と毎フレームの更新を呼ぶ。
    /// 照準（プレイヤーの頭の少し前の位置を追う）、予兆（向きを固定）、照射の順に進める。照準と予兆の間は射線に沿って危険範囲の帯を出し、照射の間は射線にいるプレイヤーへ間隔ごとにダメージを与える。
    /// </summary>
    /// <remarks>
    /// 撃つ所と傾ける中心は、フィニッシャーの体の根から一定の高さの点（pivot）。ビームは pivot から頭の位置の向きへ伸び、最初に当たった物で止まる。
    /// 頭の位置は _aimDelay 秒前の位置を狙う。照準の間だけ狙い直し、予兆と照射では最後の向きを使う。
    /// </remarks>
    public sealed class EnemyFinisherBeams : MonoBehaviour
    {
        /// <summary> ビームが何にも当たらないときの長さ（m） </summary>
        private const float MAX_BEAM_LENGTH = 1500f;

        /// <summary> 頭の位置の履歴。古い順に並ぶ </summary>
        private readonly Queue<HeadSample> _headHistory = new();

        [SerializeField]
        [Tooltip("ビームの円柱のマテリアル")]
        private Material _beamMaterial;

        [SerializeField]
        [Tooltip("照準と予兆の間に出す、危険範囲の帯のマテリアル。半透明にする")]
        private Material _telegraphMaterial;

        [SerializeField, Min(0f)]
        [Tooltip("ビームの半径（m）")]
        private float _beamRadius = 14.14f;

        [SerializeField, Min(0f)]
        [Tooltip("危険範囲の帯の半径（m）。ビームより少し広くして、避けきれたかを分かりやすくする")]
        private float _telegraphRadius = 16f;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーの頭の位置を追って向きを変える時間（秒）")]
        private float _aimDuration = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("狙うのは、この時間（秒）前のプレイヤーの頭の位置")]
        private float _aimDelay = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("向きを固定してから、ビームを撃つまでの時間（秒）")]
        private float _telegraphDuration = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("ビームを出している時間（秒）")]
        private float _firingDuration = 1f;

        [SerializeField, Min(0)]
        [Tooltip("ビームに当たっているときに、間隔ごとに与えるダメージ")]
        private int _damage = 1;

        [SerializeField, Min(0.01f)]
        [Tooltip("ダメージを与える間隔（秒）")]
        private float _damageInterval = 0.1f;

        [SerializeField]
        [Tooltip("ビームを止める物のレイヤー。プレイヤー自身のレイヤーは外す")]
        private LayerMask _blockLayers;

        private CapsuleCollider _targetCapsule;
        private Action<int> _applyDamage;
        private EnemyBeam _beam;
        private EnemyBeam _telegraph;
        private Phase _phase;

        /// <summary> 今の段階に入ってからの時間（秒） </summary>
        private float _phaseTime;

        /// <summary> 照準を始めてからの時間（秒）。頭の位置の履歴の時刻に使う </summary>
        private float _aimTime;

        /// <summary> 撃つ所から狙う位置への向き。照準の間だけ更新する </summary>
        private Vector3 _aimDirection = Vector3.down;

        /// <summary> 次にダメージを与えられるまでの時間（秒） </summary>
        private float _damageTimer;

        private bool _isReady;

        /// <summary>
        /// ビームと帯の見た目を作る。マテリアルかプレイヤーの当たり判定がなければ、警告を出して使えないままにする。
        /// </summary>
        /// <param name="target">プレイヤー。子に CapsuleCollider を持つ</param>
        /// <param name="applyDamage">ビームがプレイヤーに当たったときにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        /// <returns>使えるようになったか</returns>
        public bool Initialize(Transform target, Action<int> applyDamage)
        {
            _targetCapsule = target != null ? target.GetComponentInChildren<CapsuleCollider>() : null;
            if (_beamMaterial == null || _targetCapsule == null)
            {
                UsefulLogger.LogWarning("ビームのマテリアルかプレイヤーの CapsuleCollider がない為、フィニッシャーは吸収したあとビームを撃たずに戻ります。", this);
                return false;
            }

            if (_telegraphMaterial == null)
            {
                UsefulLogger.LogWarning("危険範囲の帯のマテリアルがない為、ビームの射線を示しません。", this);
            }

            _applyDamage = applyDamage;
            _beam = new EnemyBeam(transform, _beamMaterial);
            if (_telegraphMaterial != null) _telegraph = new EnemyBeam(transform, _telegraphMaterial);

            _isReady = true;
            return true;
        }

        /// <summary>
        /// 照準を始める。
        /// </summary>
        public void Begin()
        {
            if (!_isReady) return;

            _headHistory.Clear();
            _aimTime = 0f;
            SetPhase(Phase.Aiming);
        }

        /// <summary>
        /// 段階を進め、帯とビームを出す。EnemyFinisherAttack が毎フレーム呼ぶ。
        /// </summary>
        /// <param name="pivot">ビームを撃つ所（ワールド座標）</param>
        /// <param name="deltaTime">経過時間（秒）</param>
        /// <param name="aimDirection">pivot からビームの向き。体を傾ける向きに使う</param>
        /// <returns>まだ続くか。撃ち終えたら false を返し、帯とビームを消している</returns>
        public bool Tick(Vector3 pivot, float deltaTime, out Vector3 aimDirection)
        {
            aimDirection = _aimDirection;
            if (_phase == Phase.Idle) return false;

            _phaseTime += deltaTime;
            _aimTime += deltaTime;
            CapsuleGeometry.GetAxis(_targetCapsule, out var capsuleStart, out var capsuleEnd, out var capsuleRadius);
            var head = (Vector3)capsuleEnd + Vector3.up * capsuleRadius;

            switch (_phase)
            {
                case Phase.Aiming:
                    Aim(pivot, head);
                    ShowTelegraph(pivot);
                    if (_phaseTime >= _aimDuration) SetPhase(Phase.Telegraph);
                    break;
                case Phase.Telegraph:
                    ShowTelegraph(pivot);
                    if (_phaseTime >= _telegraphDuration) StartFiring();
                    break;
                case Phase.Firing:
                    UpdateFiring(pivot, capsuleStart, capsuleEnd, capsuleRadius, deltaTime);
                    if (_phaseTime >= _firingDuration)
                    {
                        Stop();
                        return false;
                    }

                    break;
            }

            aimDirection = _aimDirection;
            return true;
        }

        /// <summary>
        /// 帯とビームを消して終える。本体が倒れたときにも呼ぶ。
        /// </summary>
        public void Stop()
        {
            if (!_isReady) return;

            _beam.Hide();
            _telegraph?.Hide();
            SetPhase(Phase.Idle);
        }

        /// <summary>
        /// 頭の位置を履歴に足し、_aimDelay 秒前の位置へ向きを合わせる。履歴が足りない間は、最も古い位置を使う。
        /// </summary>
        private void Aim(Vector3 pivot, Vector3 head)
        {
            _headHistory.Enqueue(new HeadSample(_aimTime, head));
            while (_headHistory.Count > 1 && _headHistory.Peek().Time < _aimTime - _aimDelay)
            {
                _headHistory.Dequeue();
            }

            var toHead = _headHistory.Peek().Position - pivot;
            if (toHead.sqrMagnitude > 0f) _aimDirection = toHead.normalized;
        }

        /// <summary>
        /// 危険範囲の帯を、pivot から最初に当たった物まで出す。
        /// </summary>
        private void ShowTelegraph(Vector3 pivot)
        {
            _telegraph?.ShowBeam(pivot, CastBeam(pivot), _telegraphRadius);
        }

        private void StartFiring()
        {
            _telegraph?.Hide();
            _damageTimer = 0f;
            SetPhase(Phase.Firing);
        }

        /// <summary>
        /// ビームを pivot から最初に当たった物まで出し、プレイヤーが半径の中に入っていれば、間隔ごとにダメージを与える。
        /// </summary>
        private void UpdateFiring(Vector3 pivot, float3 capsuleStart, float3 capsuleEnd, float capsuleRadius,
            float deltaTime)
        {
            var stop = CastBeam(pivot);
            _beam.ShowBeam(pivot, stop, _beamRadius);

            _damageTimer -= deltaTime;
            if (_damageTimer > 0f) return;

            var reach = _beamRadius + capsuleRadius;
            if (CapsuleGeometry.GetSegmentDistanceSq(pivot, stop, capsuleStart, capsuleEnd, out _) > reach * reach) return;

            _damageTimer = _damageInterval;
            _applyDamage?.Invoke(_damage);
        }

        /// <summary>
        /// pivot から _aimDirection へ調べ、最初に当たった物の位置を返す。当たらなければ MAX_BEAM_LENGTH 先の位置を返す。
        /// </summary>
        private Vector3 CastBeam(Vector3 pivot)
        {
            return Physics.Raycast(pivot, _aimDirection, out var hit, MAX_BEAM_LENGTH, _blockLayers,
                QueryTriggerInteraction.Ignore)
                ? hit.point
                : pivot + _aimDirection * MAX_BEAM_LENGTH;
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            _phaseTime = 0f;
        }

        /// <summary>
        /// ビームの段階
        /// </summary>
        private enum Phase
        {
            /// <summary> 出していない </summary>
            Idle,

            /// <summary> 頭の位置を追って向きを変えている </summary>
            Aiming,

            /// <summary> 向きを固定して、撃つのを待っている </summary>
            Telegraph,

            /// <summary> ビームを撃っている </summary>
            Firing
        }

        /// <summary>
        /// ある時刻のプレイヤーの頭の位置
        /// </summary>
        private readonly struct HeadSample
        {
            public readonly float Time;
            public readonly Vector3 Position;

            public HeadSample(float time, Vector3 position)
            {
                Time = time;
                Position = position;
            }
        }
    }
}
