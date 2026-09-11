// [Legacy] 作り直しに伴い全体を無効化
#if false
using Kizami.Presentation.Runtime.Player;
using UnityEngine;

namespace Kizami.View.Runtime.Player.Player
{
    public class MobilePlayerView : MonoBehaviour, IMobilePlayerView
    {
        public Vector3 Velocity
        {
            get => _rigidbody.linearVelocity;
            set => _rigidbody.linearVelocity = value;
        }

        public Quaternion Rotation
        {
            get => transform.rotation;
            set => transform.rotation = value;
        }

        [SerializeField] private Rigidbody _rigidbody;

        public void AddForce(Vector3 force, ForceMode mode)
        {
            _rigidbody.AddForce(force, mode);
        }
    }
}
#endif
