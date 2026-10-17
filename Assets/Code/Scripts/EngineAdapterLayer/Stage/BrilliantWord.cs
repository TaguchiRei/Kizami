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
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class BrilliantWord : MonoBehaviour
    {
        private readonly List<BrilliantLetter> _letters = new();

        private Rigidbody _body;
        private BoxCollider _collider;

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
        /// 運んでいる言葉を、運んでいた大きさのまま、頭文字を進む先へ向けて、重力なしでまっすぐ飛ばす。ぶつからないまま寿命が来たら消える。
        /// 元の大きさに戻すと、運んでいる位置では地面やプレイヤーに重なる為、小さいまま飛ばす。
        /// </summary>
        /// <param name="direction">飛ばす向き（正規化済み）</param>
        /// <param name="speed">速さ（m/s）</param>
        /// <param name="lifetime">何にもぶつからないときに、消えるまでの時間（秒）</param>
        public void Launch(Vector3 direction, float speed, float lifetime)
        {
            IsCarried = false;
            IsThrown = true;
            transform.rotation = GetFlightRotation(direction);
            _body.isKinematic = false;
            _collider.enabled = true;
            _body.useGravity = false;
            _body.constraints = RigidbodyConstraints.FreezeRotation;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.linearVelocity = direction * speed;
            _body.angularVelocity = Vector3.zero;
            Destroy(gameObject, lifetime);
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

        private void OnCollisionEnter(Collision collision)
        {
            if (IsThrown) Touched?.Invoke(this, collision);
        }
    }
}
