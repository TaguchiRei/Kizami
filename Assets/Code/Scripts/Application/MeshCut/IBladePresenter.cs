using System;
using UnityEngine;

namespace Kizami.Application.Runtime
{
    public interface IBladePresenter
    {
        void SetRotation(Quaternion rotation);

        void Cut(Action onComplete);
    }
}
