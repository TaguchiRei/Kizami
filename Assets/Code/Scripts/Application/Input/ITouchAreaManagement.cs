using UnityEngine;

namespace UsefulTools.Application.Runtime.Input
{
    public interface ITouchAreaManagement
    {
        bool TryGetGroupName(Vector2 screenPosition, out string groupName);

        void Press(string groupName);

        void Release(string groupName);

        void Move(string groupName, Vector2 delta);

        void LateTick();
    }
}
