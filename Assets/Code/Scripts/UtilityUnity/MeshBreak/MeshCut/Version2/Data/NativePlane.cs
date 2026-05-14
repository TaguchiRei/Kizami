using Unity.Mathematics;
using UnityEngine;

public struct NativePlane
{
    public float3 Position;
    public float3 Normal;
    public float Distance;

    public NativePlane(float3 pos, float3 normal)
    {
        Position = pos;
        Normal = normal;
        Distance = -math.dot(Normal, Position);
    }

    public NativePlane(Transform transform)
    {
        Position = transform.position;
        Normal = transform.up;
        Distance = -math.dot(Normal, Position);
    }
}