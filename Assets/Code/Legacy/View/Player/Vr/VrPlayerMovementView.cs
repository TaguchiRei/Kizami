using UnityEngine;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;
using UsefulVr.Presentation.Runtime.Player;

namespace UsefulVr.View.Runtime.Player
{
    public class VrPlayerMovementView : InitializableMonoBehaviour, IVrMovementView
    {
        [Header("Physics")] [SerializeField] private Rigidbody _rigidbody;

        [Header("XR References")] [SerializeField]
        private Transform _xrOrigin;

        [SerializeField] private Transform _cameraOffset;
        [SerializeField] private Transform _mainCamera;

        private float _bodyYawAccumulated;

        public Vector3 Velocity
        {
            get => _rigidbody.linearVelocity;
            set => _rigidbody.linearVelocity = value;
        }

        public Quaternion Rotation
        {
            get => transform.localRotation;
            set => transform.localRotation = value;
        }

        /// <summary>
        /// PlayerRootのワールド座標
        /// </summary>
        public Vector3 ColliderPosition => _rigidbody.position;

        /// <summary>
        /// Main Cameraのワールド座標
        /// </summary>
        public Vector3 CameraPosition => _mainCamera.position;

        /// <summary>
        /// Main CameraのPlayerRoot基準ローカル座標
        /// </summary>
        public Vector3 CameraLocalPosition => _mainCamera.localPosition;

        public override void Initialize()
        {
            base.Initialize();

            _rigidbody.useGravity = false;
            gameObject.SetActive(true);
        }

        public void AddForce(Vector3 force, ForceMode mode)
        {
            if (!Initialized) return;

            _rigidbody.AddForce(force, mode);
        }

        public void ApplyPositionOffset(Vector3 offset)
        {
            if (!Initialized) return;

            // PlayerRoot を移動
            _rigidbody.MovePosition(
                _rigidbody.position + offset);

            // 視点維持のため CameraOffset を逆方向へ補正
            _cameraOffset.localPosition -= offset;
        }

        public void UpdateLeftHand(Vector3 position, Quaternion rotation)
        {
            if (!Initialized) return;
        }

        public void UpdateRightHand(Vector3 position, Quaternion rotation)
        {
            if (!Initialized) return;
        }
    }
}