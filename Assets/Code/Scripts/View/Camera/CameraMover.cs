using UnityEngine;

[DefaultExecutionOrder(10000)]
public class CameraMover : MonoBehaviour
{
    [SerializeField] private Transform _target;

    private Vector3 _positionOffset;
    private Quaternion _rotationOffset;

    private void Awake()
    {
        _positionOffset = Quaternion.Inverse(_target.rotation) * (transform.position - _target.position);

        _rotationOffset = Quaternion.Inverse(_target.rotation) * transform.rotation;
    }

    private void LateUpdate()
    {
        transform.position = _target.position + _target.rotation * _positionOffset;

        transform.rotation = _target.rotation * _rotationOffset;
    }
}