// [Legacy] 作り直しに伴い全体を無効化
#if false
using Kizami.Application.Runtime;
using Kizami.Presentation.Runtime;
using Kizami.View.Runtime.MeshCut;
using UnityEngine;
using UsefulTools.Composition.Runtime.Boot;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;

namespace Kizami.Composition.Runtime
{
    public class BladeInitializer : InitializerBase
    {
        [SerializeField] private BladeView _bladeView;

        private BladePresenter _presenter;

        private void Awake()
        {
            _presenter = new BladePresenter(_bladeView);

            InGameContainer.Register<IBladePresenter>(_presenter);
        }

        public override void Initialize()
        {
            _bladeView.Initialize();
        }
    }
}
#endif
