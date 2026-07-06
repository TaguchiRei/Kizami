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
