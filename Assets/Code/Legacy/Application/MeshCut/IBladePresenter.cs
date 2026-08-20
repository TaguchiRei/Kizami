// [Legacy] 作り直しに伴い全体を無効化
#if false
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
#endif
