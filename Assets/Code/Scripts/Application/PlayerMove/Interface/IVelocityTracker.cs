using UnityEngine;

namespace Kizami.Application.Runtime
{
    public interface IVelocityTracker
    {
        Vector3 MoveVector { get; }
        float MoveSpeed { get; }
    }
}