using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// カメラの向きを基準に、前後左右へ爆弾を1つずつ投げ、何かにぶつかった所で爆発させる Adapter。
    /// 爆弾は45度の角度で投げ、平らな地面なら飛距離の位置に落ちる。寿命が来ても爆発する。
    /// </summary>
    public sealed class BombThrowAdapter : MonoBehaviour
    {
        /// <summary> 投げる角度（度） </summary>
        private const float LAUNCH_ANGLE = 45f;

        /// <summary> 投げる向きの数。前後左右 </summary>
        private const int DIRECTION_COUNT = 4;

        private Action<Vector3, float> _onExplode;

        [SerializeField, Min(0.01f)]
        [Tooltip("爆弾の見た目と当たり判定の直径（m）")]
        private float _bombDiameter = 0.3f;

        [SerializeField, Min(0f)]
        [Tooltip("爆弾を出す位置を、カメラから投げる向きへずらす距離（m）。プレイヤー自身に当たらないようにする")]
        private float _spawnOffset = 1f;

        [SerializeField, Min(0.1f)]
        [Tooltip("何にもぶつからない爆弾が爆発するまでの時間（秒）")]
        private float _lifetime = 5f;

        /// <param name="onExplode">爆発させる関数。引数は爆発の中心と半径（m）</param>
        public void Initialize(Action<Vector3, float> onExplode)
        {
            _onExplode = onExplode;
        }

        /// <summary>
        /// カメラの位置から、水平の前後左右へ爆弾を1つずつ投げる。
        /// </summary>
        /// <param name="distance">平らな地面に落ちる距離（m）</param>
        /// <param name="radius">爆発の半径（m）</param>
        /// <param name="forwardOffset">投げる起点を、カメラの水平な正面へずらす距離（m）</param>
        public void Throw(float distance, float radius, float forwardOffset)
        {
            var cameraMain = Camera.main;
            if (cameraMain == null)
            {
                UsefulLogger.LogError("MainCamera が見つからない為、爆弾を投げられません。", this);
                return;
            }

            var origin = cameraMain.transform.position;
            var forward = Vector3.ProjectOnPlane(cameraMain.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(cameraMain.transform.up, Vector3.up);
            }

            forward.Normalize();
            origin += forward * forwardOffset;

            for (var i = 0; i < DIRECTION_COUNT; i++)
            {
                var direction = Quaternion.AngleAxis(90f * i, Vector3.up) * forward;
                SpawnBomb(origin + direction * _spawnOffset, direction, distance, radius);
            }
        }

        private void SpawnBomb(Vector3 position, Vector3 direction, float distance, float radius)
        {
            var bomb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bomb.name = "Bomb";
            bomb.transform.SetPositionAndRotation(position, Quaternion.identity);
            bomb.transform.localScale = Vector3.one * _bombDiameter;

            var body = bomb.AddComponent<Rigidbody>();
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 水平距離 = v^2 * sin(2θ) / g。45度なら v = sqrt(距離 * g)
            var speed = Mathf.Sqrt(distance * Physics.gravity.magnitude);
            var angle = LAUNCH_ANGLE * Mathf.Deg2Rad;
            body.linearVelocity = (direction * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * speed;

            var fuse = bomb.AddComponent<BombFuse>();
            fuse.Setup(_lifetime, point => _onExplode?.Invoke(point, radius));
        }

        /// <summary>
        /// 爆弾1つの導火線。ぶつかるか寿命が来ると、その位置で一度だけ爆発して爆弾を消す。
        /// </summary>
        private sealed class BombFuse : MonoBehaviour
        {
            private Action<Vector3> _onExplode;
            private float _remaining;
            private bool _isExploded;

            public void Setup(float lifetime, Action<Vector3> onExplode)
            {
                _remaining = lifetime;
                _onExplode = onExplode;
            }

            private void Update()
            {
                _remaining -= Time.deltaTime;
                if (_remaining <= 0f) Explode();
            }

            private void OnCollisionEnter(Collision collision)
            {
                Explode();
            }

            private void Explode()
            {
                if (_isExploded) return;

                _isExploded = true;
                _onExplode?.Invoke(transform.position);
                Destroy(gameObject);
            }
        }
    }
}
