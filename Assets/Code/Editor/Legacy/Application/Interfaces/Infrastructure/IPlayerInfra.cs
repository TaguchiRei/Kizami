// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using UnityEngine;

namespace Kizami.Application.Runtime.Player
{
    public interface IPlayerInfra
    {
        Vector3 Position { get; }
        event Action UpdateEvent;
    }
}
#endif
