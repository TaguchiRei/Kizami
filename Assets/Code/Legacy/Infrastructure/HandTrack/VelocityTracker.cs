using Kizami.Application.Runtime;

namespace Kizami.Infrastructure.Runtime.Player
{
    using UnityEngine;

    public class VelocityTracker : MonoBehaviour, IVelocityTracker
    {
        /// <summary>
        /// 直前1フレーム分の移動ベクトル
        /// </summary>
        public Vector3 MoveVector { get; private set; }

        /// <summary>
        /// 移動速度（unit/sec）
        /// </summary>
        public float MoveSpeed => _velocity.magnitude;

        /// <summary>
        /// 必要なら速度ベクトル自体も取得可能
        /// </summary>
        public Vector3 Velocity => _velocity;

        private Vector3 _velocity;
        private Vector3 _previousPosition;

        private void Start()
        {
            _previousPosition = transform.position;
        }

        private void Update()
        {
            Vector3 currentPosition = transform.position;

            // 1フレーム分の移動量
            MoveVector = currentPosition - _previousPosition;

            // 速度ベクトル
            _velocity = MoveVector / Time.deltaTime;

            _previousPosition = currentPosition;
        }
    }
}
