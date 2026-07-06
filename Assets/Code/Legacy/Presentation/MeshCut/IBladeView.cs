using System;
using UnityEngine;

namespace Kizami.Presentation.Runtime.MeshCut
{
    public interface IBladeView
    {
        Quaternion DefaultRotation { get; }
        void SetRotation(Quaternion rotation);

        void Cut(Action onComplete);
    }
}