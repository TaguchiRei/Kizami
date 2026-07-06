using UnityEngine;

namespace UsefulVr.Presentation.Runtime.Player
{
    /// <summary>
    /// Applicationの計算をもとにViewを動かすプレゼンター
    /// </summary>
    public class VrVrPlayerMovementPresenter : IVrPlayerPresenter
    {
        private readonly IVrMovementView _movementView;

        public Vector3 GravityVector { get; set; }
        public float GravityPower { get; set; }

        public VrVrPlayerMovementPresenter(IVrMovementView view)
        {
            _movementView = view;
        }

        public Vector3 Velocity
        {
            get => _movementView.Velocity;
            set => _movementView.Velocity = value;
        }

        public Quaternion Rotation
        {
            get => _movementView.Rotation;
            set => _movementView.Rotation = value;
        }

        public Vector3 CameraLocalPosition => _movementView.CameraLocalPosition;

        public void AddForce(Vector3 force, ForceMode mode)
        {
            _movementView.AddForce(force, mode);
        }

        public void SetOffset(Vector3 offset)
        {
            // Viewへ補正適用
            _movementView.ApplyPositionOffset(offset);
        }
    }
}