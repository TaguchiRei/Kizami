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