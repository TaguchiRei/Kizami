using System;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// Brilliant Skill の文字 1 文字。出現の拡大、プレイヤーにつかまれてカメラの前に運ばれること、投げられて飛ぶことを担う。
    /// 出たあとは Kinematic で動かず、つかまれるまで出現位置にとどまる。つかまれている間は小さくして物理を止め、投げられたら重力ありで飛ぶ。
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class BrilliantLetter : MonoBehaviour
    {
        /// <summary> 拡大が行き過ぎの頂点に届く、出現にかける時間に対する割合 </summary>
        private const float POP_PEAK_RATIO = 0.55f;

        /// <summary> 行き過ぎの頂点の大きさ </summary>
        private const float POP_PEAK_SCALE = 1.35f;

        private Rigidbody _body;
        private BoxCollider _collider;
        private float _popStartTime;
        private float _popDuration;
        private bool _isPopping;

        /// <summary> つかんだときの、カメラから見た向き </summary>
        private Quaternion _carryRotation;

        /// <summary> 投げられてから、ぶつかったときに知らせる処理 </summary>
        public event Action<BrilliantLetter, Collision> Touched;

        /// <summary> 投げられて飛んでいるか。一度投げられたらつかめない </summary>
        public bool IsThrown { get; private set; }

        /// <summary> 当たり判定の中心（ワールド空間）。当たり判定を止めている間も求められる </summary>
        public Vector3 Center => transform.TransformPoint(_collider.center);

        /// <summary>
        /// 出現の拡大を始める。大きさ 0 から行き過ぎの頂点まで広がり、1 に戻る。
        /// </summary>
        /// <param name="duration">出現にかける時間（秒）。ゲーム内の時間の倍率に関係なく進む</param>
        public void StartPop(float duration)
        {
            _popStartTime = Time.unscaledTime;
            _popDuration = duration;
            _isPopping = true;
            transform.localScale = Vector3.zero;
        }

        /// <summary>
        /// つかまれて運ばれ始める。物理と当たり判定を止め（狙う先を決めるレイにも当たらなくなる）、親を外して（組が消えても残るように）小さくし、今のカメラから見た向きを覚える。
        /// </summary>
        /// <param name="cameraTransform">つかんだプレイヤーのカメラ</param>
        /// <param name="scale">運んでいる間の大きさ</param>
        public void BeginCarry(Transform cameraTransform, float scale)
        {
            _isPopping = false;
            _carryRotation = Quaternion.Inverse(cameraTransform.rotation) * transform.rotation;
            transform.SetParent(null, true);
            transform.localScale = Vector3.one * scale;
            _body.isKinematic = true;
            _collider.enabled = false;
        }

        /// <summary>
        /// 運んでいる文字を、つかんだときのカメラから見た向きのまま、当たり判定の中心がカメラの前の指定した位置に来るように置く。
        /// </summary>
        /// <param name="cameraTransform">運んでいるプレイヤーのカメラ</param>
        /// <param name="offset">カメラのローカル座標で表した、文字の中心を置く位置（m）</param>
        public void FollowCamera(Transform cameraTransform, Vector3 offset)
        {
            transform.rotation = cameraTransform.rotation * _carryRotation;

            var centerOffset = transform.TransformPoint(_collider.center) - transform.position;
            transform.position = cameraTransform.TransformPoint(offset) - centerOffset;
        }

        /// <summary>
        /// 運んでいる文字を元の大きさに戻し、物理を戻して指定の向きへ飛ばす。ぶつからないまま寿命が来たら消える。
        /// </summary>
        /// <param name="direction">飛ばす向き（正規化済み）</param>
        /// <param name="speed">初速（m/s）</param>
        /// <param name="lifetime">何にもぶつからないときに、消えるまでの時間（秒）</param>
        public void Launch(Vector3 direction, float speed, float lifetime)
        {
            IsThrown = true;
            transform.localScale = Vector3.one;
            _body.isKinematic = false;
            _collider.enabled = true;
            _body.useGravity = true;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.linearVelocity = direction * speed;
            _body.angularVelocity = Vector3.zero;
            Destroy(gameObject, lifetime);
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _collider = GetComponent<BoxCollider>();
        }

        private void Update()
        {
            if (!_isPopping) return;

            var progress = Mathf.Clamp01((Time.unscaledTime - _popStartTime) / _popDuration);
            transform.localScale = Vector3.one * EvaluatePopScale(progress);
            if (progress >= 1f) _isPopping = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsThrown) Touched?.Invoke(this, collision);
        }

        /// <summary>
        /// 出現の進み具合（0〜1）に対する文字の大きさ。0 から行き過ぎの頂点まで勢いよく広がり、1 に戻る。
        /// </summary>
        private static float EvaluatePopScale(float progress)
        {
            if (progress < POP_PEAK_RATIO)
            {
                var rise = progress / POP_PEAK_RATIO;
                return POP_PEAK_SCALE * (1f - (1f - rise) * (1f - rise));
            }

            var settle = (progress - POP_PEAK_RATIO) / (1f - POP_PEAK_RATIO);
            return Mathf.Lerp(POP_PEAK_SCALE, 1f, settle);
        }
    }
}
