using UnityEngine;

namespace UsefulTools.Application.Runtime.Input
{
    public interface ITouchAreaManagement
    {
        bool TryGetGroupId(Vector2 screenPosition, out string groupId);

        void Press(string groupId);

        void Release(string groupId);

        void Move(string groupId, Vector2 delta);

        void LateTick();
    }
}