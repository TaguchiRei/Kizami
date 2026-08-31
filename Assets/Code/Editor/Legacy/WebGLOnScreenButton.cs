// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.OnScreen;

namespace Kizami.Composition.Runtime
{
    public class WebGLOnScreenButton : OnScreenControl, IPointerDownHandler, IPointerUpHandler
    {
        [InputControl(layout = "Button")] [SerializeField]
        private string _controlPath;

        protected override string controlPathInternal
        {
            get => _controlPath;
            set => _controlPath = value;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            SendValueToControl(1.0f);
        }

        public void OnPointerUpHandler(PointerEventData eventData)
        {
            SendValueToControl(0.0f);
        }

        // IPointerUpHandler の実装
        void IPointerUpHandler.OnPointerUp(PointerEventData eventData)
        {
            SendValueToControl(0.0f);
        }
    }
}
#endif
