using UnityEngine;

namespace UsefulVr.Presentation.Runtime.Player
{
    public interface IVrMovementView
    {
        Vector3 Velocity { get; set; }
        Quaternion Rotation { get; set; }

        Vector3 ColliderPosition { get; }

        Vector3 CameraPosition { get; }

        Vector3 CameraLocalPosition { get; }

        void AddForce(Vector3 force, ForceMode mode);

        /// <summary> PlayerRootへ補正オフセットを適用する </summary>
        void ApplyPositionOffset(Vector3 offset);
    }
}