using System;

namespace Kizami.Application.Runtime
{
    public interface IEnemyMoveInfra
    {
        event Action<float> UpdateEvent;
    }
}