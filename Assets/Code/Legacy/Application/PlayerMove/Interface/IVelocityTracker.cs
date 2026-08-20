// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEngine;

namespace Kizami.Application.Runtime
{
    public interface IVelocityTracker
    {
        Vector3 MoveVector { get; }
        float MoveSpeed { get; }
    }
}
#endif
