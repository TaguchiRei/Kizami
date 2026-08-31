// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using Kizami.Application.Runtime;
using Kizami.Presentation.Runtime.MeshCut;
using UnityEngine;

namespace Kizami.Presentation.Runtime
{
    public class BladePresenter : IBladePresenter
    {
        private IBladeView _bladeView;

        public BladePresenter(IBladeView bladeView)
        {
            _bladeView = bladeView;
        }

        public Quaternion DefaultRotation => _bladeView.DefaultRotation;

        public void SetRotation(Quaternion rotation)
        {
            _bladeView.SetRotation(rotation);
        }

        public void Cut(Action onComplete)
        {
            _bladeView.Cut(onComplete);
        }
    }
}
#endif
