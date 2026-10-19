using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスの胴体を動かし、脚を足を置く位置の切り替えと2本の骨の IK で歩かせる。ボスのボクセルのモデルの根に付ける。
    /// 胴体はプレイヤーとの水平の距離を保つように近づきながら、プレイヤーの方へ旋回する。高さは置いた高さのまま変えない。
    /// </summary>
    /// <remarks>
    /// 敵の EnemyLegs は腿が体の根の直下、脛が腿の直下にある階層を前提にしているが、ボスの脚は腰と腿・腿と脛の間に別の部位が挟まる。
    /// そのため骨の向きは、休みの姿勢の回転に戻してから、ワールド空間の向きの差だけ回して合わせる。
    /// 足場は経路の格子に乗らないので、真下へのレイで決める。
    /// </remarks>
    public sealed class BossBody : MonoBehaviour
    {
        /// <summary> IK で脚を伸ばしきる・畳みきる手前にあける長さ（m）。向きが決まらなくならないようにする </summary>
        private const float REACH_MARGIN = 0.01f;

        [SerializeField]
        [Tooltip("ボスが向かう先（プレイヤー）")]
        private Transform _target;

        [SerializeField]
        [Tooltip("脚")]
        private BossLeg[] _legs;

        [SerializeField, Min(0f)]
        [Tooltip("胴体の移動の速さ（m/s）")]
        private float _moveSpeed = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("胴体の旋回の速さ（度/s）")]
        private float _turnSpeed = 30f;

        [SerializeField, Min(0f)]
        [Tooltip("プレイヤーとの水平の距離をこの長さ（m）まで詰める")]
        private float _keepDistance = 20f;

        [SerializeField]
        [Tooltip("足を置く地面のレイヤー")]
        private LayerMask _groundLayers = ~0;

        [SerializeField, Min(0.1f)]
        [Tooltip("足場を探すレイを、足を置く基準の位置からこの高さ（m）上から下へ撃つ")]
        private float _groundRayHeight = 10f;

        [SerializeField, Min(0.01f)]
        [Tooltip("足と基準の位置の差がこの長さ（m）を超えたら、その組を運ぶ")]
        private float _stepThreshold = 2f;

        [SerializeField, Min(0.01f)]
        [Tooltip("1 組を運ぶのにかける時間（秒）")]
        private float _stepDuration = 0.6f;

        [SerializeField, Min(0f)]
        [Tooltip("運ぶ途中で足を持ち上げる高さ（m）")]
        private float _stepHeight = 1.5f;

        /// <summary> 脚ごとの、休みの姿勢の腿の回転（親の空間） </summary>
        private Quaternion[] _upperRestRotations;

        /// <summary> 脚ごとの、休みの姿勢の脛の回転（親の空間） </summary>
        private Quaternion[] _lowerRestRotations;

        /// <summary> 脚ごとの、腿の長さ（腰から膝まで） </summary>
        private float[] _upperLengths;

        /// <summary> 脚ごとの、脛の長さ（膝から足先まで） </summary>
        private float[] _lowerLengths;

        /// <summary> 脚ごとの、足を置く基準の位置（胴体の根の空間。休みの姿勢の足先） </summary>
        private Vector3[] _homes;

        /// <summary> 脚ごとの、地面に置いている足の位置（ワールド） </summary>
        private Vector3[] _planted;

        /// <summary> 脚ごとの、運んでいる足の運ぶ前の位置（ワールド） </summary>
        private Vector3[] _stepFrom;

        /// <summary> 脚ごとの、運んでいる足の運ぶ先（ワールド） </summary>
        private Vector3[] _stepTo;

        /// <summary> 脚ごとの、設定が正しく IK で動かせるか </summary>
        private bool[] _isValid;

        /// <summary> 運んでいる組の番号。どの組も運んでいなければ -1 </summary>
        private int _steppingPair = -1;

        /// <summary> 運んでいる組の、運び始めてからの割合（0〜1） </summary>
        private float _stepProgress;

        private Vector3 _previousRootPosition;

        /// <summary> 胴体を動かすか。止めても脚は足を置き直す </summary>
        private bool _isMoving = true;

        /// <summary>
        /// 向かう先を target にする。ボスとプレイヤーが別のシーンにあるときに、初期化で渡す。
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        /// <summary>
        /// 胴体の移動と旋回を切り替える。止めている間も、脚は今の胴体に合わせて足を運ぶ。
        /// </summary>
        /// <param name="isMoving">動かすなら true</param>
        public void SetMoving(bool isMoving)
        {
            _isMoving = isMoving;
        }

        private void Awake()
        {
            var count = _legs.Length;
            _upperRestRotations = new Quaternion[count];
            _lowerRestRotations = new Quaternion[count];
            _upperLengths = new float[count];
            _lowerLengths = new float[count];
            _homes = new Vector3[count];
            _planted = new Vector3[count];
            _stepFrom = new Vector3[count];
            _stepTo = new Vector3[count];
            _isValid = new bool[count];

            for (var i = 0; i < count; i++)
            {
                var leg = _legs[i];
                if (leg.Upper == null || leg.Lower == null || leg.Foot == null ||
                    !leg.Lower.IsChildOf(leg.Upper) || !leg.Foot.IsChildOf(leg.Lower) || !leg.Upper.IsChildOf(transform))
                {
                    UsefulLogger.LogError($"ボスの脚 {i} は、胴体の子孫に腿、腿の子孫に脛、脛の子孫に足先を置く必要があります。", this);
                    continue;
                }

                _upperRestRotations[i] = leg.Upper.localRotation;
                _lowerRestRotations[i] = leg.Lower.localRotation;
                _upperLengths[i] = Vector3.Distance(leg.Upper.position, leg.Lower.position);
                _lowerLengths[i] = Vector3.Distance(leg.Lower.position, leg.Foot.position);
                _homes[i] = transform.InverseTransformPoint(leg.Foot.position);
                _isValid[i] = _upperLengths[i] > REACH_MARGIN && _lowerLengths[i] > REACH_MARGIN;
            }
        }

        private void Start()
        {
            for (var i = 0; i < _legs.Length; i++)
            {
                if (_isValid[i]) _planted[i] = GetHomePosition(i);
            }

            _previousRootPosition = transform.position;
            Pose();
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            if (_target != null && _isMoving) Move(deltaTime);

            var velocity = (transform.position - _previousRootPosition) / deltaTime;
            velocity.y = 0f;
            _previousRootPosition = transform.position;

            if (_steppingPair < 0) TryStartStep(velocity);
            if (_steppingPair >= 0)
            {
                _stepProgress += deltaTime / _stepDuration;
                if (_stepProgress >= 1f)
                {
                    for (var i = 0; i < _legs.Length; i++)
                    {
                        if (_isValid[i] && _legs[i].Pair == _steppingPair) _planted[i] = _stepTo[i];
                    }

                    _steppingPair = -1;
                }
            }

            Pose();
        }

        /// <summary>
        /// プレイヤーの方へ旋回し、水平の距離が保つ距離より遠ければ前へ進む。
        /// </summary>
        private void Move(float deltaTime)
        {
            var toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-4f) return;

            var targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _turnSpeed * deltaTime);

            var distance = toTarget.magnitude;
            if (distance <= _keepDistance) return;

            var step = Mathf.Min(_moveSpeed * deltaTime, distance - _keepDistance);
            var forward = transform.forward;
            forward.y = 0f;
            transform.position += forward.normalized * step;
        }

        /// <summary>
        /// 運んでいる組がないとき、足と基準の位置の差が最も大きい組を選び、その差が踏み出す距離を超えていれば運び始める。
        /// 運ぶ先は、基準の位置から胴体の速さで運ぶ時間の半分だけ先回りした位置。
        /// </summary>
        private void TryStartStep(Vector3 velocity)
        {
            var pair = -1;
            var largest = _stepThreshold * _stepThreshold;

            for (var i = 0; i < _legs.Length; i++)
            {
                if (!_isValid[i]) continue;

                var offset = _planted[i] - GetHomePosition(i);
                offset.y = 0f;
                if (offset.sqrMagnitude <= largest) continue;

                largest = offset.sqrMagnitude;
                pair = _legs[i].Pair;
            }

            if (pair < 0) return;

            var lead = velocity * (_stepDuration * 0.5f);
            for (var i = 0; i < _legs.Length; i++)
            {
                if (!_isValid[i] || _legs[i].Pair != pair) continue;

                _stepFrom[i] = _planted[i];
                _stepTo[i] = GetGroundPosition(transform.TransformPoint(_homes[i]) + lead);
            }

            _steppingPair = pair;
            _stepProgress = 0f;
        }

        /// <summary>
        /// 足を置く基準の位置（ワールド）。高さは真下の地面。
        /// </summary>
        private Vector3 GetHomePosition(int i)
        {
            return GetGroundPosition(transform.TransformPoint(_homes[i]));
        }

        /// <summary>
        /// 位置の真下の地面の位置。地面がなければ、位置の高さのまま返す。
        /// </summary>
        private Vector3 GetGroundPosition(Vector3 position)
        {
            var origin = position + Vector3.up * _groundRayHeight;
            return BossGroundProbe.TryGetGround(transform, origin, _groundRayHeight * 2f, _groundLayers, out var ground)
                ? ground
                : position;
        }

        /// <summary>
        /// 足の位置（運んでいる組は、運ぶ前と運ぶ先の間を、持ち上げながら進めた位置）へ、各脚を IK で向ける。
        /// </summary>
        private void Pose()
        {
            for (var i = 0; i < _legs.Length; i++)
            {
                if (!_isValid[i]) continue;

                var foot = _planted[i];
                if (_legs[i].Pair == _steppingPair)
                {
                    var t = Mathf.Clamp01(_stepProgress);
                    foot = Vector3.Lerp(_stepFrom[i], _stepTo[i], t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * _stepHeight);
                }

                Solve(i, foot);
            }
        }

        /// <summary>
        /// 脚 i の足先を、ワールドの位置 target へ向ける。届かなければ届く所まで伸ばし、近すぎれば畳める所までにする。
        /// 膝は、腰から足先への向きに対して上側へ曲げる。腿と脛は休みの姿勢の回転に戻してから、今の向きと求めた向きの差だけ回す。
        /// </summary>
        private void Solve(int i, Vector3 target)
        {
            var leg = _legs[i];
            leg.Upper.localRotation = _upperRestRotations[i];
            leg.Lower.localRotation = _lowerRestRotations[i];

            var upperLength = _upperLengths[i];
            var lowerLength = _lowerLengths[i];
            var hip = leg.Upper.position;
            var toTarget = target - hip;
            var distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + REACH_MARGIN,
                upperLength + lowerLength - REACH_MARGIN);
            var direction = toTarget.sqrMagnitude > 0f ? toTarget.normalized : Vector3.down;

            // 膝を曲げる向きは、腰から足先への向きに垂直な成分のうち、上向きのもの
            var bend = Vector3.up - Vector3.Dot(Vector3.up, direction) * direction;
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : transform.forward;

            var cosHip = Mathf.Clamp((upperLength * upperLength + distance * distance - lowerLength * lowerLength)
                                     / (2f * upperLength * distance), -1f, 1f);
            var sinHip = Mathf.Sqrt(1f - cosHip * cosHip);
            var knee = hip + upperLength * (cosHip * direction + sinHip * bend);
            var foot = hip + direction * distance;

            leg.Upper.rotation = Quaternion.FromToRotation(leg.Lower.position - hip, knee - hip) * leg.Upper.rotation;
            leg.Lower.rotation = Quaternion.FromToRotation(leg.Foot.position - leg.Lower.position, foot - knee) *
                                 leg.Lower.rotation;
        }
    }
}
