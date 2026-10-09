using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 体を貸した敵の脚を、足を置く位置の切り替えと 2 本の骨の IK で歩かせる。敵の体のプレハブの根に付け、EnemySpawnAdapter が毎フレーム呼ぶ。
    /// </summary>
    public sealed class EnemyLegs : MonoBehaviour
    {
        /// <summary> IK で脚を伸ばしきる・畳みきる手前にあける長さ（m）。向きが決まらなくならないようにする </summary>
        private const float REACH_MARGIN = 0.001f;

        [SerializeField]
        [Tooltip("脚")]
        private EnemyLeg[] _legs;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("足を置く基準の位置を、腰から休みの姿勢の足先の水平の向きへ、その水平の距離のこの割合だけ離す")]
        private float _reachRate = 0.7f;

        [SerializeField]
        [Tooltip("足を置く基準の位置を、腰より外側（左右）へずらす長さ（m）")]
        private float _splay = 0.7f;

        [SerializeField, Min(0.01f)]
        [Tooltip("足と基準の位置の差がこの長さ（m）を超えたら、その組を運ぶ")]
        private float _stepThreshold = 0.9f;

        [SerializeField, Min(0.01f)]
        [Tooltip("1 組を運ぶのにかける時間（秒）")]
        private float _stepDuration = 0.35f;

        [SerializeField, Min(0f)]
        [Tooltip("運ぶ途中で足を持ち上げる高さ（m）")]
        private float _stepHeight = 0.6f;

        /// <summary> 脚ごとの、腰の位置（体の根の空間） </summary>
        private Vector3[] _hips;

        /// <summary> 脚ごとの、休みの姿勢で腰から膝へ向かう向き（体の根の空間、単位ベクトル） </summary>
        private Vector3[] _axes;

        /// <summary> 脚ごとの、休みの姿勢で膝から脛の先へ向かう向き（体の根の空間、単位ベクトル） </summary>
        private Vector3[] _lowerAxes;

        /// <summary> 脚ごとの、腿の長さ（腰から膝まで） </summary>
        private float[] _upperLengths;

        /// <summary> 脚ごとの、足を置く基準の位置（体の根の空間。高さは根の高さ） </summary>
        private Vector3[] _homes;

        /// <summary> 脚ごとの、休みの姿勢の足先の位置（体の根の空間） </summary>
        private Vector3[] _restFeet;

        private MeshFilter[] _upperMeshes;
        private MeshFilter[] _lowerMeshes;

        /// <summary> 脚ごとの、地面に置いている足の位置（ワールド） </summary>
        private Vector3[] _planted;

        /// <summary> 脚ごとの、運んでいる足の運ぶ前の位置（ワールド） </summary>
        private Vector3[] _stepFrom;

        /// <summary> 脚ごとの、運んでいる足の運ぶ先（ワールド） </summary>
        private Vector3[] _stepTo;

        /// <summary> 脚ごとの、足を留めているか。留めた足は運ばない </summary>
        private bool[] _isHeld;

        /// <summary> 脚ごとの、足を留める位置（ワールド） </summary>
        private Vector3[] _heldTargets;

        /// <summary> 運んでいる組の番号。どの組も運んでいなければ -1 </summary>
        private int _steppingPair = -1;

        /// <summary> 運んでいる組の、運び始めてからの割合（0〜1） </summary>
        private float _stepProgress;

        private Vector3 _previousRootPosition;

        /// <summary> 脚の数 </summary>
        public int LegCount => _legs.Length;

        /// <summary>
        /// メッシュの範囲のうち、基準点から向き axis（単位ベクトル）へ最も遠い所までの長さ。メッシュがなければ 0。
        /// </summary>
        private static float GetMeshLength(MeshFilter meshFilter, Vector3 axis)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null) return 0f;

            var bounds = meshFilter.sharedMesh.bounds;
            var extents = bounds.extents;
            return Mathf.Max(0f, Vector3.Dot(bounds.center, axis) + Mathf.Abs(axis.x) * extents.x +
                                 Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z);
        }

        /// <summary>
        /// 体を貸したときに呼ぶ。留めていた足を放し、足を基準の位置に置き、脚の姿勢を合わせる。
        /// </summary>
        public void ResetFeet(in EnemyNavigationGrid grid)
        {
            for (var i = 0; i < _legs.Length; i++)
            {
                _isHeld[i] = false;
                _planted[i] = GetHomePosition(i, grid);
            }

            _steppingPair = -1;
            _previousRootPosition = transform.position;
            Pose();
        }

        /// <summary>
        /// 毎フレーム、体の根の位置と向きを合わせたあとに呼ぶ。足を運び、脚の姿勢を合わせる。
        /// </summary>
        public void UpdateLegs(float deltaTime, in EnemyNavigationGrid grid)
        {
            if (deltaTime <= 0f) return;

            var velocity = (transform.position - _previousRootPosition) / deltaTime;
            velocity.y = 0f;
            _previousRootPosition = transform.position;

            if (_steppingPair < 0) TryStartStep(velocity, grid);
            if (_steppingPair >= 0)
            {
                _stepProgress += deltaTime / _stepDuration;
                if (_stepProgress >= 1f)
                {
                    for (var i = 0; i < _legs.Length; i++)
                    {
                        if (_legs[i].Pair == _steppingPair && !_isHeld[i]) _planted[i] = _stepTo[i];
                    }

                    _steppingPair = -1;
                }
            }

            Pose();
        }

        /// <summary>
        /// 体を返すときに呼ぶ。留めていた足を放し、脚を休みの姿勢に戻す。
        /// </summary>
        public void ResetPose()
        {
            for (var i = 0; i < _legs.Length; i++)
            {
                _isHeld[i] = false;
                if (_legs[i].Upper != null) _legs[i].Upper.localRotation = Quaternion.identity;
                if (_legs[i].Lower != null) _legs[i].Lower.localRotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// 脚 leg の足を、歩いて運ぶ代わりに位置 target（ワールド）へ伸ばす。体が動く間も留め続けるときは、毎フレーム呼び直す。
        /// </summary>
        /// <param name="leg">脚の番号（設定の並び）</param>
        /// <param name="target">足を伸ばす先（ワールド座標）</param>
        public void HoldFoot(int leg, Vector3 target)
        {
            if (leg < 0 || leg >= _legs.Length) return;

            _isHeld[leg] = true;
            _heldTargets[leg] = target;
        }

        /// <summary>
        /// 脚 leg の休みの姿勢の足先の位置（ワールド座標）を返す。体が浮いている間、HoldFoot に渡して脚を垂らす。
        /// </summary>
        /// <param name="leg">脚の番号（設定の並び）</param>
        public Vector3 GetRestFootPosition(int leg)
        {
            return transform.TransformPoint(_restFeet[leg]);
        }

        /// <summary>
        /// 留めていた足を放し、基準の位置に置き直す。留めていなければ何もしない。
        /// </summary>
        /// <param name="leg">脚の番号（設定の並び）</param>
        /// <param name="grid">足を置く高さを読む経路の格子</param>
        public void ReleaseFoot(int leg, in EnemyNavigationGrid grid)
        {
            if (leg < 0 || leg >= _legs.Length || !_isHeld[leg]) return;

            _isHeld[leg] = false;
            _planted[leg] = GetHomePosition(leg, grid);
        }

        private void Awake()
        {
            var count = _legs.Length;
            _hips = new Vector3[count];
            _axes = new Vector3[count];
            _lowerAxes = new Vector3[count];
            _upperLengths = new float[count];
            _homes = new Vector3[count];
            _restFeet = new Vector3[count];
            _upperMeshes = new MeshFilter[count];
            _lowerMeshes = new MeshFilter[count];
            _planted = new Vector3[count];
            _stepFrom = new Vector3[count];
            _stepTo = new Vector3[count];
            _isHeld = new bool[count];
            _heldTargets = new Vector3[count];

            for (var i = 0; i < count; i++)
            {
                var leg = _legs[i];
                if (leg.Upper == null || leg.Lower == null || leg.Upper.parent != transform || leg.Lower.parent != leg.Upper)
                {
                    UsefulLogger.LogError($"脚 {i} の腿は体の根の直下に、脛は腿の直下に置く必要があります。", this);
                    continue;
                }

                var knee = leg.Lower.localPosition;
                _hips[i] = leg.Upper.localPosition;
                _axes[i] = knee.normalized;
                _lowerAxes[i] = leg.LowerRestDirection == Vector3.zero ? _axes[i] : leg.LowerRestDirection.normalized;
                _upperLengths[i] = knee.magnitude;
                _upperMeshes[i] = leg.Upper.GetComponent<MeshFilter>();
                _lowerMeshes[i] = leg.Lower.GetComponent<MeshFilter>();

                // 休みの姿勢の足先の、腰から見た位置
                var restFoot = knee + _lowerAxes[i] * GetMeshLength(_lowerMeshes[i], _lowerAxes[i]);
                _restFeet[i] = _hips[i] + restFoot;
                var side = Mathf.Sign(_hips[i].x) * _splay;
                _homes[i] = new Vector3(_hips[i].x + restFoot.x * _reachRate + side, 0f,
                    _hips[i].z + restFoot.z * _reachRate);
            }
        }

        /// <summary>
        /// 運んでいる組がないとき、足と基準の位置の差が最も大きい組を選び、その差が踏み出す距離を超えていれば運び始める。
        /// 運ぶのは 1 組ずつで、四足なら対角の 2 本ずつのトロットになる。運ぶ先は、基準の位置から体の速さで運ぶ時間の半分だけ先回りした位置。
        /// </summary>
        private void TryStartStep(Vector3 velocity, in EnemyNavigationGrid grid)
        {
            var pair = -1;
            var largest = _stepThreshold * _stepThreshold;

            for (var i = 0; i < _legs.Length; i++)
            {
                if (!IsUpperAttached(i) || _isHeld[i]) continue;

                var offset = _planted[i] - GetHomePosition(i, grid);
                offset.y = 0f;
                if (offset.sqrMagnitude <= largest) continue;

                largest = offset.sqrMagnitude;
                pair = _legs[i].Pair;
            }

            if (pair < 0) return;

            var lead = velocity * (_stepDuration * 0.5f);
            for (var i = 0; i < _legs.Length; i++)
            {
                if (_legs[i].Pair != pair || _isHeld[i]) continue;

                _stepFrom[i] = _planted[i];
                var to = transform.TransformPoint(_homes[i]) + lead;
                to.y = GetGroundHeight(to, grid);
                _stepTo[i] = to;
            }

            _steppingPair = pair;
            _stepProgress = 0f;
        }

        /// <summary>
        /// 足を置く基準の位置（ワールド）。高さは、その位置の格子の立てる層のうち、体の根と同じ高さの層。
        /// </summary>
        private Vector3 GetHomePosition(int i, in EnemyNavigationGrid grid)
        {
            var home = transform.TransformPoint(_homes[i]);
            home.y = GetGroundHeight(home, grid);
            return home;
        }

        /// <summary>
        /// 位置の真下の格子の立てる層のうち、体の根との高さの差が登れる高さ以内の層の高さ。なければ体の根の高さ。
        /// 敵ごとにレイを撃たずに済むよう、地面の高さは経路の格子から取る。
        /// </summary>
        private float GetGroundHeight(Vector3 position, in EnemyNavigationGrid grid)
        {
            var rootHeight = transform.position.y;
            if (!grid.TryGetColumn(position, out var column)) return rootHeight;

            var node = grid.GetNodeNear(column, rootHeight);
            return node >= 0 ? grid.Heights[node] : rootHeight;
        }

        private bool IsUpperAttached(int i)
        {
            return _upperMeshes[i] != null && _legs[i].Upper.gameObject.activeSelf;
        }

        /// <summary>
        /// 足の位置（留めた足は留める位置、運んでいる組は、運ぶ前と運ぶ先の間を、持ち上げながら進めた位置）へ、各脚を IK で向ける。腿を失った脚は動かさない。
        /// </summary>
        private void Pose()
        {
            for (var i = 0; i < _legs.Length; i++)
            {
                if (!IsUpperAttached(i)) continue;

                var foot = _planted[i];
                if (_isHeld[i])
                {
                    foot = _heldTargets[i];
                }
                else if (_legs[i].Pair == _steppingPair)
                {
                    var t = Mathf.Clamp01(_stepProgress);
                    foot = Vector3.Lerp(_stepFrom[i], _stepTo[i], t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * _stepHeight);
                }

                Solve(i, transform.InverseTransformPoint(foot));
            }
        }

        /// <summary>
        /// 脚 i の足先を、体の根の空間の位置 target へ向ける。届かなければ届く所まで伸ばし、近すぎれば畳める所までにする。
        /// 膝は、腰から足先への向きに対して上側へ曲げ、腿と脛の向きは休みの姿勢の向きからの最小の回転で決める。脛を失っていれば、腿だけを target へ向ける。
        /// 脛の長さは毎回メッシュの範囲から求めるので、切られて短くなった脛も、届く所まで伸ばして動く。
        /// </summary>
        private void Solve(int i, Vector3 target)
        {
            var leg = _legs[i];
            var hip = _hips[i];
            var axis = _axes[i];
            var toTarget = target - hip;

            if (!leg.Lower.gameObject.activeSelf)
            {
                leg.Upper.localRotation = Quaternion.FromToRotation(axis, toTarget);
                return;
            }

            var lowerAxis = _lowerAxes[i];
            var upperLength = _upperLengths[i];
            var lowerLength = Mathf.Max(GetMeshLength(_lowerMeshes[i], lowerAxis), REACH_MARGIN);
            var distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + REACH_MARGIN,
                upperLength + lowerLength - REACH_MARGIN);
            var direction = toTarget.sqrMagnitude > 0f ? toTarget.normalized : axis;

            // 膝を曲げる向きは、腰から足先への向きに垂直な成分のうち、上向きのもの
            var bend = Vector3.up - Vector3.Dot(Vector3.up, direction) * direction;
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : axis;

            var cosHip = Mathf.Clamp((upperLength * upperLength + distance * distance - lowerLength * lowerLength)
                                     / (2f * upperLength * distance), -1f, 1f);
            var sinHip = Mathf.Sqrt(1f - cosHip * cosHip);
            var knee = hip + upperLength * (cosHip * direction + sinHip * bend);
            var foot = hip + direction * distance;

            var upperRotation = Quaternion.FromToRotation(axis, knee - hip);
            leg.Upper.localRotation = upperRotation;
            leg.Lower.localRotation = Quaternion.Inverse(upperRotation) * Quaternion.FromToRotation(lowerAxis, foot - knee);
        }
    }
}
