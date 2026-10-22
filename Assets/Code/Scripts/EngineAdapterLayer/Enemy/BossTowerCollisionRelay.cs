using System;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 塔の Rigidbody に付いて、子のコライダーが何かに触れた通知を BossTowerThrow へ渡す。
    /// </summary>
    public sealed class BossTowerCollisionRelay : MonoBehaviour
    {
        private Action<Collision> _onCollision;

        /// <summary>
        /// 触れたときに呼ぶ処理を設定する。
        /// </summary>
        public void Initialize(Action<Collision> onCollision)
        {
            _onCollision = onCollision;
        }

        private void OnCollisionEnter(Collision collision)
        {
            _onCollision?.Invoke(collision);
        }
    }
}
