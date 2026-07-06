using System;
using UnityEngine;

namespace Kizami.Application.Runtime
{
    public interface IBladePresenter
    {
        Quaternion DefaultRotation { get;}
        void SetRotation(Quaternion rotation);

        void Cut(Action onComplete);
    }
}