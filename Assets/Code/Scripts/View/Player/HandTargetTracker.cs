using UnityEngine;

namespace UsefulVr.View.Runtime.Player
{
    public class HandTargetTracker : MonoBehaviour
    {
        [Header("XR Tracker")] [SerializeField]
        private Transform _tracker;

        [Header("Offset")] [SerializeField] private Vector3 _positionOffset;
        [SerializeField] private Vector3 _rotationOffsetEuler;

        void LateUpdate()
        {
            if (_tracker == null) return;

            transform.position =
                _tracker.TransformPoint(_positionOffset);

            transform.rotation =
                _tracker.rotation *
                Quaternion.Euler(_rotationOffsetEuler);
        }
    }
}