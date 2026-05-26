using System;
using UnityEngine;

namespace Kizami.Presentation.Runtime.MeshCut
{
    public interface IBladeView
    {
        void SetRotation(Quaternion rotation);

        void Cut(Action onComplete);
    }
}
