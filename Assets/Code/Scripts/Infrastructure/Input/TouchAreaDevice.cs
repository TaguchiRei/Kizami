using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;

namespace UsefulTools.Infrastructure.Runtime
{
    [InputControlLayout(displayName = "TouchAreaDevice")]
    public class TouchAreaDevice : InputDevice
    {
        public ButtonControl Press { get; private set; }

        public Vector2Control Delta { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            InputSystem.RegisterLayout<TouchAreaDevice>();
        }

        protected override void FinishSetup()
        {
            base.FinishSetup();

            Press = GetChildControl<ButtonControl>("Press");

            Delta = GetChildControl<Vector2Control>("Delta");
        }
    }
}