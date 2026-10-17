using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// Brilliant Skill の言葉全体（同時に出た複数行を含む）。文字の形に依存しない 1 つの箱の当たり判定を持ち、つかむ的・足場・飛んでいくときの当たり判定に使う。
    /// 文字（BrilliantLetter）は子で、当たり判定を持たない。通常攻撃の切断は、この箱に当たったら子の文字を切る対象にする。
    /// 出たあとは Kinematic で動かず、つかまれるまで出現位置にとどまる。
    /// つかまれている間は小さくして物理を止め、読む向きの先頭（頭文字の側）をカメラの前方へ向けて運ぶ。
    /// 投げられたら重力を無視してまっすぐ飛び、頭文字を進む先へ向ける（プレイヤーからは文字が読めない向き）。
    /// 飛びながら出たときの大きさに戻る。戻る間は当たり判定を止め、地面に重ならない高さへ持ち上げる。
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class BrilliantWord : MonoBehaviour
    {
        /// <summary> 投げるときに、真下の面を探すレイの長さ（m） </summary>
        private const float LIFT_RAY_DISTANCE = 200f;

        private readonly List<BrilliantLetter> _letters = new();

        private Rigidbody _body;
        private BoxCollider _collider;
        private bool _isGrowing;
        private float _growStartTime;
        private float _growDuration;
        private float _growFromScale;
        private float _liftTotal;
        private float _liftApplied;
        private Vector3 _launchDirection;
        private float _launchSpeed;

        /// <summary> つかんだときの、カメラから見た向き </summary>
        private Quaternion _carryRotation;

        /// <summary> 投げられてから、ぶつかったときに知らせる処理 </summary>
        public event Action<BrilliantWord, Collision> Touched;

        /// <summary> つかまれて運ばれているか </summary>
        public bool IsCarried { get; private set; }

        /// <summary> 投げられて飛んでいるか。一度投げられたらつかめない </summary>
        public bool IsThrown { get; private set; }

        /// <summary> 切られずに残っている文字 </summary>
        public IReadOnlyList<BrilliantLetter> Letters => _letters;

        /// <summary> 当たり判定の箱（ローカル空間）の最も長い辺の長さ（m）。大きさ 1 のときの言葉の長さ </summary>
        public float Length => Mathf.Max(_collider.size.x, _collider.size.y, _collider.size.z);

        /// <summary> 当たり判定を止めている間も求められる、箱の中心（ワールド空間） </summary>
        public Vector3 Center => transform.TransformPoint(_collider.center);

        /// <summary> 当たり判定の箱。つかむ的を調べるのに使う </summary>
        public BoxCollider Box => _collider;

        /// <summary>
        /// 文字を言葉に加える。加えた文字を、箱が包むようにする。
        /// </summary>
        /// <param name="letter">子にした文字</param>
        /// <param name="localBounds">言葉のローカル空間で表した、文字のメッシュの範囲</param>
        public void AddLetter(BrilliantLetter letter, Bounds localBounds)
        {
            letter.Word = this;
            _letters.Add(letter);

            if (_letters.Count == 1)
            {
                _collider.center = localBounds.center;
                _collider.size = localBounds.size;
                return;
            }

            var bounds = new Bounds(_collider.center, _collider.size);
            bounds.Encapsulate(localBounds);
            _collider.center = bounds.center;
            _collider.size = bounds.size;
        }

        /// <summary>
        /// 切られて消える文字を外す。文字が残らなければ、言葉ごと消える。
        /// </summary>
        /// <param name="letter">外す文字</param>
        public void RemoveLetter(BrilliantLetter letter)
        {
            _letters.Remove(letter);
            if (_letters.Count == 0) Destroy(gameObject);
        }

        /// <summary>
        /// つかまれて運ばれ始める。物理と当たり判定を止め、親を外して（組が消えても残るように）最も長い辺が length になる大きさに縮める。
        /// 運ぶ間の向きは、読む向きの先頭（ローカルの -X 側）がカメラの前方へ向く、文字が立つ向きにする。
        /// </summary>
        /// <param name="length">運んでいる間の言葉の長さ（m）</param>
        public void BeginCarry(float length)
        {
            IsCarried = true;
            _carryRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            transform.SetParent(null, true);
            transform.localScale = Vector3.one * (length / Length);
            _body.isKinematic = true;
            _collider.enabled = false;
        }

        /// <summary>
        /// 運んでいる言葉を、カメラから見た向きを保ち、当たり判定の中心がカメラの前の指定した位置に来るように置く。
        /// </summary>
        /// <param name="cameraTransform">運んでいるプレイヤーのカメラ</param>
        /// <param name="offset">カメラのローカル座標で表した、言葉の中心を置く位置（m）</param>
        public void FollowCamera(Transform cameraTransform, Vector3 offset)
        {
            transform.rotation = cameraTransform.rotation * _carryRotation;

            var centerOffset = transform.TransformPoint(_collider.center) - transform.position;
            transform.position = cameraTransform.TransformPoint(offset) - centerOffset;
        }

        /// <summary>
        /// 運んでいる言葉を、頭文字を進む先へ向けて、重力なしでまっすぐ飛ばす。ぶつからないまま寿命が来たら消える。
        /// 運んでいる位置で出たときの大きさに戻すと、地面やプレイヤーに重なる為、growDuration の間に飛びながら元の大きさに戻す。
        /// 戻る間は当たり判定を止め、戻った大きさの箱の下端が、真下にある面から clearance 以上離れる高さまで持ち上げる。
        /// </summary>
        /// <param name="direction">飛ばす向き（正規化済み）</param>
        /// <param name="speed">速さ（m/s）</param>
        /// <param name="lifetime">元の大きさに戻ってから、何にもぶつからないときに消えるまでの時間（秒）</param>
        /// <param name="growDuration">元の大きさに戻るまでの時間（秒）</param>
        /// <param name="clearance">真下にある面から空ける高さ（m）</param>
        public void Launch(Vector3 direction, float speed, float lifetime, float growDuration, float clearance)
        {
            IsCarried = false;
            IsThrown = true;
            transform.rotation = GetFlightRotation(direction);
            _collider.enabled = false;
            _launchDirection = direction;
            _launchSpeed = speed;
            _growFromScale = transform.localScale.x;
            _growStartTime = Time.time;
            _growDuration = growDuration;
            _liftTotal = CalculateLift(clearance);
            _liftApplied = 0f;
            _isGrowing = true;
            Destroy(gameObject, lifetime + growDuration);
        }

        /// <summary>
        /// 元の大きさに戻り、当たり判定を戻して、重力なしの物理でまっすぐ飛ぶようにする。
        /// </summary>
        private void FinishGrowth()
        {
            _isGrowing = false;
            transform.localScale = Vector3.one;
            _collider.enabled = true;
            _body.isKinematic = false;
            _body.useGravity = false;
            _body.constraints = RigidbodyConstraints.FreezeRotation;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.linearVelocity = _launchDirection * _launchSpeed;
            _body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// 元の大きさに戻った箱の下端を、真下にある面から clearance 離すのに要る持ち上げる高さを返す。真下に何もなければ 0。
        /// 箱の下端は、今の向きの箱の、ワールドの Y 方向の広がりから求める。
        /// </summary>
        private float CalculateLift(float clearance)
        {
            var center = Center;
            if (!Physics.Raycast(center, Vector3.down, out var hit, LIFT_RAY_DISTANCE, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return 0f;
            }

            var size = _collider.size;
            var halfHeight = 0.5f * (Mathf.Abs((transform.rotation * Vector3.right).y) * size.x +
                                     Mathf.Abs((transform.rotation * Vector3.up).y) * size.y +
                                     Mathf.Abs((transform.rotation * Vector3.forward).y) * size.z);
            var bottom = center.y - halfHeight;
            return Mathf.Max(0f, hit.point.y + clearance - bottom);
        }

        /// <summary>
        /// 読む向きの先頭（ローカルの -X 側）が direction を向き、文字が立つ向きを返す。
        /// </summary>
        private static Quaternion GetFlightRotation(Vector3 direction)
        {
            var up = Vector3.ProjectOnPlane(Vector3.up, direction);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.forward, direction);

            up.Normalize();
            var readingDirection = -direction;
            return Quaternion.LookRotation(Vector3.Cross(readingDirection, up), up);
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _collider = GetComponent<BoxCollider>();
        }

        private void Update()
        {
            if (!_isGrowing) return;

            var progress = Mathf.Clamp01((Time.time - _growStartTime) / Mathf.Max(_growDuration, 0.0001f));
            var remaining = 1f - progress;
            var eased = 1f - remaining * remaining * remaining;
            transform.localScale = Vector3.one * Mathf.Lerp(_growFromScale, 1f, eased);

            var lift = _liftTotal * eased - _liftApplied;
            _liftApplied += lift;
            transform.position += _launchDirection * (_launchSpeed * Time.deltaTime) + Vector3.up * lift;
            if (progress >= 1f) FinishGrowth();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsThrown) Touched?.Invoke(this, collision);
        }
    }
}
