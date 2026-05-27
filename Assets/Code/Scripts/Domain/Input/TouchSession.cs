using UnityEngine;

namespace UsefulTools.Domain.Runtime.Input
{
    public sealed class TouchSession
    {
        public int TouchId { get; }

        public string GroupId { get; }

        public Vector2 LastPosition { get; private set; }

        public TouchSession(int touchId, string groupId, Vector2 startPosition)
        {
            TouchId = touchId;
            GroupId = groupId;
            LastPosition = startPosition;
        }

        public Vector2 UpdatePosition(Vector2 position)
        {
            Vector2 delta = position - LastPosition;

            LastPosition = position;

            return delta;
        }
    }
}