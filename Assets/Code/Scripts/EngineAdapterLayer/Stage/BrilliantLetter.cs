using System;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// Brilliant Skill の文字 1 文字。出現の拡大、プレイヤーにつかまれてカメラの前に運ばれること、投げられて飛ぶことを担う。
    /// 出るときは、遅らせた時刻から、大きくなりながら回り、縮んで元の大きさと向きに落ち着く。出てから少しの間は、敵や建物より手前に描く（深度テストを Always にした材質）。
    /// 出たあとは Kinematic で動かず、つかまれるまで出現位置にとどまる。つかまれている間は小さくして物理を止め、投げられたら小さいまま重力ありで飛ぶ。
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class BrilliantLetter : MonoBehaviour
    {
        /// <summary> 最も大きくなる時点の、出現にかける時間に対する割合 </summary>
        private const float POP_PEAK_RATIO = 0.5f;

        private Rigidbody _body;
        private BoxCollider _collider;
        private float _popStartTime;
        private float _popDuration;
        private float _popPeakScale;
        private float _popSpinDegrees;
        private float _frontDuration;
        private bool _isPopping;
        private bool _isInFront;
        private Quaternion _baseRotation;
        private Renderer _fillRenderer;
        private Renderer _outlineRenderer;
        private Material _fillMaterial;
        private Material _outlineMaterial;

        /// <summary> つかんだときの、カメラから見た向き </summary>
        private Quaternion _carryRotation;

        /// <summary> 投げられてから、ぶつかったときに知らせる処理 </summary>
        public event Action<BrilliantLetter, Collision> Touched;

        /// <summary> 投げられて飛んでいるか。一度投げられたらつかめない </summary>
        public bool IsThrown { get; private set; }

        /// <summary> 当たり判定の中心（ワールド空間）。当たり判定を止めている間も求められる </summary>
        public Vector3 Center => transform.TransformPoint(_collider.center);

        /// <summary> 当たり判定の高さ（ローカル空間, m）。大きさ 1 のときの文字の高さ </summary>
        public float Height => _collider.size.y;

        /// <summary>
        /// 出現の演出を始める。遅らせた時刻まで大きさ 0 で待ち、そこから大きさ 0 → peakScale → 1 と変わり、向きは spinDegrees 回った所から元の向きまで回る。
        /// 時間はゲーム内の時間の倍率に関係なく進む。
        /// </summary>
        /// <param name="delay">演出を始めるまでの時間（秒）</param>
        /// <param name="duration">演出にかける時間（秒）</param>
        /// <param name="peakScale">最も大きくなったときの大きさ</param>
        /// <param name="spinDegrees">演出の始めに、元の向きから文字の面の中で回っている角度（度）</param>
        public void StartPop(float delay, float duration, float peakScale, float spinDegrees)
        {
            _popStartTime = Time.unscaledTime + delay;
            _popDuration = duration;
            _popPeakScale = peakScale;
            _popSpinDegrees = spinDegrees;
            _baseRotation = transform.rotation;
            _isPopping = true;
            transform.localScale = Vector3.zero;
        }

        /// <summary>
        /// 出てから少しの間、敵や建物より手前に描く。時間が過ぎたら、普通に描く材質に戻す。
        /// </summary>
        /// <param name="fillRenderer">文字の Renderer</param>
        /// <param name="outlineRenderer">輪郭線の殻の Renderer</param>
        /// <param name="fillOverlay">手前に描く文字の材質</param>
        /// <param name="outlineOverlay">手前に描く輪郭線の材質</param>
        /// <param name="fillNormal">普通に描く文字の材質</param>
        /// <param name="outlineNormal">普通に描く輪郭線の材質</param>
        /// <param name="frontDuration">手前に描く時間（秒）。出現の演出の始まりから数える</param>
        public void SetFrontPhase(Renderer fillRenderer, Renderer outlineRenderer, Material fillOverlay,
            Material outlineOverlay, Material fillNormal, Material outlineNormal, float frontDuration)
        {
            _fillRenderer = fillRenderer;
            _outlineRenderer = outlineRenderer;
            _fillMaterial = fillNormal;
            _outlineMaterial = outlineNormal;
            _frontDuration = frontDuration;
            _isInFront = true;
            _fillRenderer.sharedMaterial = fillOverlay;
            _outlineRenderer.sharedMaterial = outlineOverlay;
        }

        /// <summary>
        /// つかまれて運ばれ始める。物理と当たり判定を止め（狙う先を決めるレイにも当たらなくなる）、親を外して（組が消えても残るように）小さくし、今のカメラから見た向きを覚える。
        /// </summary>
        /// <param name="cameraTransform">つかんだプレイヤーのカメラ</param>
        /// <param name="height">運んでいる間の文字の高さ（m）</param>
        public void BeginCarry(Transform cameraTransform, float height)
        {
            _isPopping = false;
            EndFrontPhase();
            _carryRotation = Quaternion.Inverse(cameraTransform.rotation) * transform.rotation;
            transform.SetParent(null, true);
            transform.localScale = Vector3.one * (height / Height);
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
        /// 運んでいる文字の物理を戻して、運んでいた大きさのまま指定の向きへ飛ばす。ぶつからないまま寿命が来たら消える。
        /// 元の大きさに戻すと、運んでいる位置では地面やプレイヤーに重なる為、小さいまま飛ばす。
        /// </summary>
        /// <param name="direction">飛ばす向き（正規化済み）</param>
        /// <param name="speed">初速（m/s）</param>
        /// <param name="lifetime">何にもぶつからないときに、消えるまでの時間（秒）</param>
        public void Launch(Vector3 direction, float speed, float lifetime)
        {
            IsThrown = true;
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
            if (_isInFront && Time.unscaledTime - _popStartTime >= _frontDuration) EndFrontPhase();
            if (!_isPopping) return;

            var elapsed = Time.unscaledTime - _popStartTime;
            if (elapsed < 0f) return;

            var progress = Mathf.Clamp01(elapsed / _popDuration);
            transform.localScale = Vector3.one * EvaluatePopScale(progress, _popPeakScale);
            var spin = _popSpinDegrees * (1f - EaseOut(progress));
            transform.rotation = _baseRotation * Quaternion.AngleAxis(spin, Vector3.forward);
            if (progress < 1f) return;

            transform.rotation = _baseRotation;
            _isPopping = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsThrown) Touched?.Invoke(this, collision);
        }

        /// <summary>
        /// 手前に描く材質から、普通に描く材質に戻す。
        /// </summary>
        private void EndFrontPhase()
        {
            if (!_isInFront) return;

            _isInFront = false;
            _fillRenderer.sharedMaterial = _fillMaterial;
            _outlineRenderer.sharedMaterial = _outlineMaterial;
        }

        /// <summary>
        /// 出現の進み具合（0〜1）に対する文字の大きさ。0 から peakScale まで勢いよく大きくなり、1 まで縮む。
        /// </summary>
        private static float EvaluatePopScale(float progress, float peakScale)
        {
            if (progress < POP_PEAK_RATIO)
            {
                return peakScale * EaseOut(progress / POP_PEAK_RATIO);
            }

            var settle = (progress - POP_PEAK_RATIO) / (1f - POP_PEAK_RATIO);
            return Mathf.Lerp(peakScale, 1f, settle * settle * (3f - 2f * settle));
        }

        private static float EaseOut(float progress)
        {
            var remaining = 1f - progress;
            return 1f - remaining * remaining * remaining;
        }
    }
}
