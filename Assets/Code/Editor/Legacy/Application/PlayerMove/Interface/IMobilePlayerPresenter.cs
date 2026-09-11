// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEngine;

namespace Kizami.Application.Runtime
{
    public interface IMobilePlayerPresenter
    {
        /// <summary> Rigidbody.linerVelocity </summary>
        Vector3 Velocity { get; set; }

        /// <summary> Transform.rotation </summary>
        Quaternion Rotation { get; set; }
        
        void AddForce(Vector3 force, ForceMode mode);
    }
}
#endif
