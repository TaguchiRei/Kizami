using UnityEngine;

namespace UsefulTools.Domain.Runtime
{
    public readonly struct TouchInputData
    {
        public int TouchId { get; }

        public Vector2 ScreenPosition { get; }

        public TouchInputData(int touchId, Vector2 screenPosition)
        {
            TouchId = touchId;
            ScreenPosition = screenPosition;
        }
    }
}