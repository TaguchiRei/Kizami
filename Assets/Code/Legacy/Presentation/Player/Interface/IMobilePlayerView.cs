// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEngine;

namespace Kizami.Presentation.Runtime.Player
{
    public interface IMobilePlayerView
    {
        Vector3 Velocity { get; set; }

        Quaternion Rotation { get; set; }

        void AddForce(Vector3 force, ForceMode mode);
    }
}
#endif
