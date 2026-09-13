using Unity.Mathematics;
using UnityEngine;

namespace Kizami.Voxel.DebugTools
{
    /// <summary>
    /// 起動時に、同じ GameObject の VoxelObject へ解析的な形状を流し込む検証用コンポーネント。
    /// </summary>
    [RequireComponent(typeof(VoxelObject))]
    public sealed class VoxelDebugPrimitive : MonoBehaviour
    {
        private enum PrimitiveType
        {
            Box,
            Sphere,
            Capsule,
            BoxWithSphere
        }

        [SerializeField]
        [Tooltip("流し込む形状")]
        private PrimitiveType _type = PrimitiveType.BoxWithSphere;

        [SerializeField]
        [Tooltip("形状を収める範囲の大きさ（ローカル空間）")]
        private Vector3 _size = Vector3.one;

        private void Start()
        {
            var voxelObject = GetComponent<VoxelObject>();
            var half = (float3)_size * 0.5f;

            voxelObject.CreateVolume(VoxelBounds.FromCenterExtents(float3.zero, half));

            switch (_type)
            {
                case PrimitiveType.Box:
                    voxelObject.ApplyEdit(new BoxShape(float3.zero, half), VoxelCsgOperation.Union);
                    break;

                case PrimitiveType.Sphere:
                    voxelObject.ApplyEdit(new SphereShape(float3.zero, math.cmin(half)), VoxelCsgOperation.Union);
                    break;

                case PrimitiveType.Capsule:
                    var radius = math.min(half.x, half.z);
                    voxelObject.ApplyEdit(new CapsuleShape(
                            new float3(0f, -half.y + radius, 0f),
                            new float3(0f, half.y - radius, 0f),
                            radius),
                        VoxelCsgOperation.Union);
                    break;

                case PrimitiveType.BoxWithSphere:
                    voxelObject.ApplyEdit(new BoxShape(
                            new float3(0f, -half.y * 0.5f, 0f),
                            new float3(half.x, half.y * 0.5f, half.z)),
                        VoxelCsgOperation.Union);
                    voxelObject.ApplyEdit(new SphereShape(float3.zero, math.cmin(half) * 0.8f),
                        VoxelCsgOperation.Union);
                    break;
            }
        }
    }
}
