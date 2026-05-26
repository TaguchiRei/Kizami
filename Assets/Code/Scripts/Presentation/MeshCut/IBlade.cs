using System;
using UnityEngine;

namespace Kizami.Presentation.Runtime.MeshCut
{
    public interface IBlade
    {
        void SetRotation(Quaternion rotation);

        void Cut(Action onComplete);
    }
}
