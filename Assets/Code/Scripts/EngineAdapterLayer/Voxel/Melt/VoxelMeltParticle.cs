using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 融解した粒 1 個。
    /// </summary>
    public struct VoxelMeltParticle
    {
        /// <summary> 位置（ワールド空間） </summary>
        public float3 Position;

        /// <summary> 速度（ワールド空間, m/s） </summary>
        public float3 Velocity;

        /// <summary> 温度 </summary>
        public float Temperature;

        /// <summary> 体積（ワールド空間, m³） </summary>
        public float Volume;

        /// <summary> 凝固点より冷えて、動きが止まったか </summary>
        public bool IsFrozen;

        /// <summary> 直近の当たり判定で、床やボクセルの面に触れていたか </summary>
        public bool IsTouching;

        /// <summary> 体積が Volume の球の半径（ワールド空間, m） </summary>
        public float Radius => math.pow(Volume * (3f / (4f * math.PI)), 1f / 3f);
    }

    /// <summary>
    /// 粒ごとに、体積の等しい球として描く為の行列を作り、全ての粒を囲む範囲を求める。
    /// 表示するメッシュは直径 1 の球である前提。
    /// </summary>
    [BurstCompile]
    public struct VoxelMeltParticleMatrixJob : IJob
    {
        [ReadOnly] public NativeArray<VoxelMeltParticle> Particles;
        [WriteOnly] public NativeArray<Matrix4x4> Matrices;

        /// <summary> 0 番目に範囲の最小、1 番目に最大を書く </summary>
        [WriteOnly] public NativeArray<float3> Bounds;

        public void Execute()
        {
            var min = new float3(float.PositiveInfinity);
            var max = new float3(float.NegativeInfinity);

            for (var i = 0; i < Particles.Length; i++)
            {
                var particle = Particles[i];
                var radius = particle.Radius;

                Matrices[i] = float4x4.TRS(particle.Position, quaternion.identity, new float3(radius * 2f));
                min = math.min(min, particle.Position - radius);
                max = math.max(max, particle.Position + radius);
            }

            Bounds[0] = min;
            Bounds[1] = max;
        }
    }
}
