using UnityEngine;

namespace UsefulTools.Domain.Runtime.Input
{
    public sealed class TouchSession
    {
        public int TouchId { get; }

        public string GroupName { get; }

        private Vector2 _lastPosition;

        public TouchSession(int touchId, string groupName, Vector2 startPosition)
        {
            TouchId = touchId;
            GroupName = groupName;
            _lastPosition = startPosition;
        }

        public Vector2 UpdatePosition(Vector2 currentPosition)
        {
            Vector2 delta = currentPosition - _lastPosition;
            _lastPosition = currentPosition;
            return delta;
        }
    }
}
