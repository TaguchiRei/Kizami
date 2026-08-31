// [Legacy] 作り直しに伴い全体を無効化
#if false
using Unity.Mathematics;
using UnityEngine;

public struct NativeTransform
{
    public float3 Position;
    public quaternion Rotation;
    public float3 Scale;

    public NativeTransform(float3 position, quaternion rotation, float3 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public NativeTransform(Transform transform)
    {
        Position = transform.position;
        Rotation = transform.rotation;
        Scale = transform.localScale;
    }
}
#endif
