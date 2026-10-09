using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 吸収を終えたフィニッシャーの分身・予兆・ビームを扱う。EnemyFinisherAttack と同じ GameObject に置き、EnemyFinisherAttack が初期化と毎フレームの更新を呼ぶ。
    /// 分身を出して本体をプレイヤーの上へ向かわせ、着いたら真下に攻撃の範囲をデカールで示し、一定時間後に本体が特大のビームを撃つ。
    /// 同時に、本体の周りを回る分身が細いビームを撃ち、その当たる所がプレイヤーを追う。追い終えたら分身とデカールを消して終える。
    /// </summary>
    /// <remarks>
    /// 分身はフィニッシャーの見た目を色違いにしたプレハブで、当たり判定（Armor レイヤー）と ArmorPanel を持ち、装甲と同じく壊せる。
    /// 敵の状態（EnemyAgent）を持たないので、グループにも移動にも敵の数にも入らない。壊れた分身のビームは止まる。
    /// ビームは撃つ所から下へ向かい、最初に当たった物で止まる。特大のビームは範囲が広いので、撃つ所の高さからプレイヤーの頭まで真下へ調べ、物があれば遮られたとする。
    /// </remarks>
    public sealed class EnemyFinisherBeams : MonoBehaviour
    {
        /// <summary> 飛ぶ先にこの距離（m）まで近づいたら、着いたとみなす </summary>
        private const float ARRIVE_DISTANCE = 0.1f;

        /// <summary> ビームが何にも当たらないときの長さ（m） </summary>
        private const float MAX_BEAM_LENGTH = 200f;

        private readonly List<ArmorPanel> _clones = new();
        private readonly List<Rigidbody> _cloneBodies = new();

        [SerializeField]
        [Tooltip("分身のプレハブ。根に ArmorPanel、当たり判定（Armor レイヤー）、Kinematic の Rigidbody を付ける")]
        private ArmorPanel _clonePrefab;

        [SerializeField, Min(0)]
        [Tooltip("分身の数")]
        private int _cloneCount = 4;

        [SerializeField]
        [Tooltip("ビームの円柱のマテリアル")]
        private Material _beamMaterial;

        [SerializeField]
        [Tooltip("攻撃の範囲を示すデカールのプレハブ。投影の奥行き（size の z）はこのプレハブの値を使う")]
        private DecalProjector _decalPrefab;

        [SerializeField, Min(0f)]
        [Tooltip("本体が浮く、プレイヤーからの高さ（m）")]
        private float _height = 25f;

        [SerializeField, Min(0f)]
        [Tooltip("本体が狙う位置にこの距離（m）まで近づいたら、プレイヤーを追うのをやめて位置を決める")]
        private float _lockDistance = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("ビームを撃つ所の、本体と分身の体の根からの高さ（m）")]
        private float _bodyCenterHeight = 5f;

        [SerializeField, Min(0f)]
        [Tooltip("分身が本体の周りを回る半径（m）")]
        private float _orbitRadius = 8f;

        [SerializeField]
        [Tooltip("分身が本体の周りを回る速さ（度/秒）")]
        private float _orbitSpeed = 60f;

        [SerializeField, Min(0.01f)]
        [Tooltip("分身が本体の位置から回る半径まで広がるのにかける時間（秒）")]
        private float _spreadDuration = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("範囲を示してから、特大のビームを撃つまでの時間（秒）")]
        private float _telegraphDuration = 2f;

        [SerializeField, Min(0f)]
        [Tooltip("特大のビームの半径（m）")]
        private float _giantRadius = 4f;

        [SerializeField, Min(0)]
        [Tooltip("特大のビームのダメージ。1 回撃つあいだに 1 回だけ与える")]
        private int _giantDamage = 40;

        [SerializeField, Min(0f)]
        [Tooltip("特大のビームを出している時間（秒）")]
        private float _giantDuration = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("追いかけるビームの半径（m）")]
        private float _trackingRadius = 1f;

        [SerializeField, Min(0)]
        [Tooltip("追いかけるビームのダメージ。当たっている間、間隔ごとに与える")]
        private int _trackingDamage = 10;

        [SerializeField, Min(0.01f)]
        [Tooltip("追いかけるビームがダメージを与える間隔（秒）")]
        private float _trackingInterval = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("追いかけるビームを出している時間（秒）")]
        private float _trackingDuration = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("追いかけるビームの当たる所が、プレイヤーへ向かう速さ（m/s）")]
        private float _trackingSpeed = 4f;

        [SerializeField]
        [Tooltip("ビームを止める物のレイヤー。プレイヤー自身のレイヤーは外す")]
        private LayerMask _blockLayers;

        private CapsuleCollider _targetCapsule;
        private Action<int> _applyDamage;
        private EnemyBeam _giantBeam;
        private Phase _phase;

        /// <summary> 分身ごとの、追いかけるビーム。並びは _clones と同じ </summary>
        private EnemyBeam[] _trackingBeams;

        /// <summary> 分身ごとの、追いかけるビームの当たる所（水平の位置）。並びは _clones と同じ </summary>
        private Vector2[] _trackingPoints;

        /// <summary> 分身ごとの、追いかけるビームが次にダメージを与えられるまでの時間（秒）。並びは _clones と同じ </summary>
        private float[] _trackingTimers;

        /// <summary> 今の段階に入ってからの時間（秒） </summary>
        private float _phaseTime;

        /// <summary> 分身を出してからの時間（秒） </summary>
        private float _cloneTime;

        /// <summary> 本体が狙う位置を決めたか。決めるまでは毎フレーム、プレイヤーの上へ狙い直す </summary>
        private bool _isLocked;

        /// <summary> 本体が浮く位置（体の根） </summary>
        private Vector3 _hoverPosition;

        /// <summary> 特大のビームが止まる所 </summary>
        private Vector3 _giantStop;

        /// <summary> 今回の特大のビームがプレイヤーに当たったか </summary>
        private bool _hasGiantHit;

        private bool _isReady;

        /// <summary>
        /// 分身とビームの見た目をプールに作る。プレハブ・マテリアル・プレイヤーの当たり判定のどれかがなければ、警告を出して使えないままにする。
        /// </summary>
        /// <param name="target">プレイヤー。子に CapsuleCollider を持つ</param>
        /// <param name="applyDamage">ビームがプレイヤーに当たったときにダメージを与える関数（PlayerHealthService.ApplyDamage）。引数はダメージ量</param>
        /// <returns>使えるようになったか</returns>
        public bool Initialize(Transform target, Action<int> applyDamage)
        {
            _targetCapsule = target != null ? target.GetComponentInChildren<CapsuleCollider>() : null;
            if (_clonePrefab == null || _beamMaterial == null || _targetCapsule == null)
            {
                UsefulLogger.LogWarning("分身のプレハブ・ビームのマテリアル・プレイヤーの CapsuleCollider のどれかがない為、フィニッシャーは吸収したあと分身せずに戻ります。", this);
                return false;
            }

            if (_decalPrefab == null)
            {
                UsefulLogger.LogWarning("デカールのプレハブがない為、ビームの範囲を示しません。", this);
            }

            _applyDamage = applyDamage;
            _giantBeam = new EnemyBeam(transform, _beamMaterial, _decalPrefab);
            _trackingBeams = new EnemyBeam[_cloneCount];
            _trackingPoints = new Vector2[_cloneCount];
            _trackingTimers = new float[_cloneCount];

            for (var i = 0; i < _cloneCount; i++)
            {
                var clone = Instantiate(_clonePrefab, transform);
                clone.gameObject.SetActive(false);
                _clones.Add(clone);
                _cloneBodies.Add(clone.GetComponent<Rigidbody>());
                _trackingBeams[i] = new EnemyBeam(transform, _beamMaterial, _decalPrefab);
            }

            _isReady = true;
            return true;
        }

        /// <summary>
        /// 本体の位置に分身を出し、本体をプレイヤーの上へ向かわせ始める。
        /// </summary>
        /// <param name="mainPosition">本体の体の根の位置</param>
        public void Begin(Vector3 mainPosition)
        {
            if (!_isReady) return;

            for (var i = 0; i < _clones.Count; i++)
            {
                _clones[i].transform.position = mainPosition;
                _clones[i].gameObject.SetActive(true);
                _clones[i].SetDurability(_clones[i].MaxDurability);
            }

            _cloneTime = 0f;
            _isLocked = false;
            SetPhase(Phase.Rising);
        }

        /// <summary>
        /// 段階を進め、分身を本体の周りで回し、ビームを撃つ。EnemyFinisherAttack が毎フレーム呼ぶ。
        /// </summary>
        /// <param name="mainPosition">本体の体の根の位置</param>
        /// <param name="deltaTime">経過時間（秒）</param>
        /// <param name="flyTarget">本体が向かう位置（体の根）</param>
        /// <returns>まだ続くか。撃ち終えたら false を返し、分身とデカールを消している</returns>
        public bool Tick(Vector3 mainPosition, float deltaTime, out Vector3 flyTarget)
        {
            flyTarget = mainPosition;
            if (_phase == Phase.Idle) return false;

            _phaseTime += deltaTime;
            _cloneTime += deltaTime;
            CapsuleGeometry.GetAxis(_targetCapsule, out var capsuleStart, out var capsuleEnd, out var capsuleRadius);

            switch (_phase)
            {
                case Phase.Rising:
                    UpdateRising(mainPosition, capsuleStart);
                    break;
                case Phase.Telegraph:
                    if (_phaseTime >= _telegraphDuration) StartFiring();
                    break;
                case Phase.Firing:
                    UpdateGiantBeam(capsuleStart, capsuleEnd, capsuleRadius);
                    UpdateTrackingBeams(capsuleStart, capsuleEnd, capsuleRadius, deltaTime);
                    if (_phaseTime >= Mathf.Max(_giantDuration, _trackingDuration))
                    {
                        Stop();
                        return false;
                    }

                    break;
            }

            flyTarget = _hoverPosition;
            MoveClones(mainPosition, capsuleStart);
            return true;
        }

        /// <summary>
        /// 分身・ビーム・デカールを消して終える。本体が倒れたときにも呼ぶ。
        /// </summary>
        public void Stop()
        {
            if (!_isReady) return;

            foreach (var clone in _clones)
            {
                clone.gameObject.SetActive(false);
            }

            _giantBeam.Hide();
            foreach (var beam in _trackingBeams)
            {
                beam.Hide();
            }

            SetPhase(Phase.Idle);
        }

        /// <summary>
        /// 位置を決めるまではプレイヤーの上を狙い直し、決めた位置に着いたら、特大のビームが止まる所に範囲を示す。
        /// </summary>
        private void UpdateRising(Vector3 mainPosition, Vector3 capsuleStart)
        {
            if (!_isLocked)
            {
                _hoverPosition = capsuleStart + Vector3.up * _height;
                _isLocked = Vector3.Distance(mainPosition, _hoverPosition) <= _lockDistance;
                return;
            }

            if (Vector3.Distance(mainPosition, _hoverPosition) > ARRIVE_DISTANCE) return;

            var emitter = _hoverPosition + Vector3.up * _bodyCenterHeight;
            _giantStop = CastBeam(emitter, Vector3.down, MAX_BEAM_LENGTH);
            _giantBeam.ShowDecal(_giantStop, _giantRadius);
            SetPhase(Phase.Telegraph);
        }

        /// <summary>
        /// 特大のビームと追いかけるビームを撃ち始める。追いかけるビームの当たる所は、各分身の真下から始める。
        /// </summary>
        private void StartFiring()
        {
            _hasGiantHit = false;
            for (var i = 0; i < _clones.Count; i++)
            {
                var position = _clones[i].transform.position;
                _trackingPoints[i] = new Vector2(position.x, position.z);
                _trackingTimers[i] = 0f;
            }

            SetPhase(Phase.Firing);
        }

        /// <summary>
        /// 特大のビームを出している間、円柱を出し、プレイヤーが範囲の中にいて、撃つ所の高さから頭まで物がなければ、1 回だけダメージを与える。出し終えたら円柱とデカールを消す。
        /// </summary>
        private void UpdateGiantBeam(Vector3 capsuleStart, Vector3 capsuleEnd, float capsuleRadius)
        {
            if (_phaseTime > _giantDuration)
            {
                _giantBeam.Hide();
                return;
            }

            var emitter = _hoverPosition + Vector3.up * _bodyCenterHeight;
            _giantBeam.ShowBeam(emitter, _giantStop, _giantRadius);
            if (_hasGiantHit) return;

            var horizontal = new Vector2(capsuleStart.x - emitter.x, capsuleStart.z - emitter.z);
            var reach = _giantRadius + capsuleRadius;
            if (horizontal.sqrMagnitude > reach * reach || capsuleEnd.y >= emitter.y) return;

            // ビームと同じく上から下へ調べ、頭より上に物があれば屋根の下に隠れているとして当てない。撃つ所を包む物は、調べ始めに重なるので数えない
            var head = capsuleEnd + Vector3.up * capsuleRadius;
            var above = new Vector3(head.x, emitter.y, head.z);
            if (Physics.Raycast(above, Vector3.down, emitter.y - head.y, _blockLayers, QueryTriggerInteraction.Ignore)) return;

            _hasGiantHit = true;
            _applyDamage?.Invoke(_giantDamage);
        }

        /// <summary>
        /// 残っている分身ごとに、ビームの当たる所をプレイヤーへ近づけ、分身からそこまでの円柱とデカールを出し、プレイヤーが円柱に触れていれば間隔ごとにダメージを与える。
        /// 当たる所は、その位置の真下で最初に当たる物の上。分身からそこまでの間に物があれば、そこで止まる。壊れた分身のビームは消す。
        /// </summary>
        private void UpdateTrackingBeams(Vector3 capsuleStart, Vector3 capsuleEnd, float capsuleRadius, float deltaTime)
        {
            var player = new Vector2(capsuleStart.x, capsuleStart.z);
            var hitReach = _trackingRadius + capsuleRadius;

            for (var i = 0; i < _clones.Count; i++)
            {
                var beam = _trackingBeams[i];
                if (_phaseTime > _trackingDuration || !_clones[i].gameObject.activeSelf)
                {
                    beam.Hide();
                    continue;
                }

                _trackingPoints[i] = Vector2.MoveTowards(_trackingPoints[i], player, _trackingSpeed * deltaTime);
                _trackingTimers[i] -= deltaTime;

                var emitter = _clones[i].transform.position + Vector3.up * _bodyCenterHeight;
                var above = new Vector3(_trackingPoints[i].x, emitter.y, _trackingPoints[i].y);
                var aim = CastBeam(above, Vector3.down, MAX_BEAM_LENGTH);
                var toAim = aim - emitter;
                var stop = CastBeam(emitter, toAim.normalized, toAim.magnitude);

                beam.ShowBeam(emitter, stop, _trackingRadius);
                beam.ShowDecal(stop, _trackingRadius);

                if (_trackingTimers[i] > 0f) continue;
                if (CapsuleGeometry.GetSegmentDistanceSq(emitter, stop, capsuleStart, capsuleEnd, out _) > hitReach * hitReach) continue;

                _trackingTimers[i] = _trackingInterval;
                _applyDamage?.Invoke(_trackingDamage);
            }
        }

        /// <summary>
        /// 残っている分身を、本体の周りの回る半径の上へ動かし、プレイヤーへ向ける。半径は出してから広がる時間をかけて広げる。
        /// </summary>
        private void MoveClones(Vector3 mainPosition, Vector3 capsuleStart)
        {
            var radius = _orbitRadius * Mathf.Clamp01(_cloneTime / _spreadDuration);
            for (var i = 0; i < _clones.Count; i++)
            {
                if (!_clones[i].gameObject.activeSelf) continue;

                var angle = math.radians(360f * i / _clones.Count + _orbitSpeed * _cloneTime);
                var position = mainPosition + new Vector3(math.cos(angle), 0f, math.sin(angle)) * radius;
                var toPlayer = capsuleStart - position;
                toPlayer.y = 0f;
                var rotation = toPlayer.sqrMagnitude > 0f ? Quaternion.LookRotation(toPlayer) : _clones[i].transform.rotation;

                if (_cloneBodies[i] != null)
                {
                    _cloneBodies[i].MovePosition(position);
                    _cloneBodies[i].MoveRotation(rotation);
                }
                else
                {
                    _clones[i].transform.SetPositionAndRotation(position, rotation);
                }
            }
        }

        /// <summary>
        /// origin から direction へ length まで調べ、最初に当たった物の位置を返す。当たらなければ length 先の位置を返す。
        /// </summary>
        private Vector3 CastBeam(Vector3 origin, Vector3 direction, float length)
        {
            return Physics.Raycast(origin, direction, out var hit, length, _blockLayers, QueryTriggerInteraction.Ignore)
                ? hit.point
                : origin + direction * length;
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            _phaseTime = 0f;
        }

        private void OnDrawGizmos()
        {
            if (_phase != Phase.Telegraph && _phase != Phase.Firing) return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_giantStop, _giantRadius);
        }

        /// <summary>
        /// 分身とビームの段階
        /// </summary>
        private enum Phase
        {
            /// <summary> 出していない </summary>
            Idle,

            /// <summary> 本体がプレイヤーの上へ向かっている </summary>
            Rising,

            /// <summary> 特大のビームの範囲を示している </summary>
            Telegraph,

            /// <summary> 特大のビームと追いかけるビームを撃っている </summary>
            Firing
        }
    }
}
