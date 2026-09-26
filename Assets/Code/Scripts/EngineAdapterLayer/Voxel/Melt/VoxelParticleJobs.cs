using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 面に触れた粒の速度を更新する計算。
    /// </summary>
    public static class VoxelParticleContact
    {
        /// <summary>
        /// 面へ向かう速度を消し、面に沿う速度を温度に応じて減らす。温度が高いほどよく滑る。
        /// 面に触れたことを記録する。
        /// </summary>
        /// <param name="particle">更新する粒</param>
        /// <param name="normal">面の法線（ワールド空間, 正規化済み）</param>
        /// <param name="deltaTime">経過時間</param>
        /// <param name="hotDamping">蒸発点での、面に沿う速度の 1 秒あたりの減り方</param>
        /// <param name="coldDamping">凝固点での、面に沿う速度の 1 秒あたりの減り方</param>
        /// <param name="freezeTemperature">凝固点</param>
        /// <param name="evaporationTemperature">蒸発点</param>
        public static void Apply(ref VoxelMeltParticle particle, float3 normal, float deltaTime,
            float hotDamping, float coldDamping, float freezeTemperature, float evaporationTemperature)
        {
            var normalSpeed = math.dot(particle.Velocity, normal);
            if (normalSpeed < 0f) particle.Velocity -= normal * normalSpeed;

            var range = math.max(evaporationTemperature - freezeTemperature, 1e-3f);
            var heat = math.saturate((particle.Temperature - freezeTemperature) / range);
            var damping = math.lerp(coldDamping, hotDamping, heat);
            particle.Velocity *= math.max(1f - damping * deltaTime, 0f);
            particle.IsTouching = true;
        }
    }

    /// <summary>
    /// 粒を冷やし、重力で動かす。蒸発点以上の粒には印を付ける。
    /// 凝固点より冷えた粒は、前回の当たり判定で面に触れていれば止める。
    /// 空中で止めると宙に浮いたまま固まる為、空中の粒は冷えても落ち続け、面に触れたフレームの次に止まる。
    /// 面に触れたかの記録は、ここで消してから当たり判定で付け直す。
    /// </summary>
    [BurstCompile]
    public struct VoxelParticleIntegrateJob : IJobParallelFor
    {
        public NativeArray<VoxelMeltParticle> Particles;
        public float DeltaTime;
        public float3 Gravity;

        /// <summary> 速度の上限（m/s）。1 フレームの移動がボクセル数個分より大きくなると、面をすり抜ける </summary>
        public float MaxSpeed;

        public float CoolingPerSecond;
        public float FreezeTemperature;
        public float EvaporationTemperature;

        /// <summary> 蒸発した粒に 1 を書く </summary>
        [WriteOnly] public NativeArray<byte> Evaporated;

        public void Execute(int index)
        {
            var particle = Particles[index];

            if (particle.Temperature >= EvaporationTemperature)
            {
                Evaporated[index] = 1;
                return;
            }

            Evaporated[index] = 0;
            if (particle.IsFrozen) return;

            particle.Temperature = math.max(particle.Temperature - CoolingPerSecond * DeltaTime, 0f);
            if (particle.Temperature < FreezeTemperature && particle.IsTouching)
            {
                particle.IsFrozen = true;
                particle.Velocity = float3.zero;
                Particles[index] = particle;
                return;
            }

            particle.IsTouching = false;

            var velocity = particle.Velocity + Gravity * DeltaTime;
            var speed = math.length(velocity);
            if (speed > MaxSpeed) velocity *= MaxSpeed / speed;

            particle.Velocity = velocity;
            particle.Position += velocity * DeltaTime;
            Particles[index] = particle;
        }
    }

    /// <summary>
    /// 粒の今の位置から、このフレームに進む先（重力を含む）の、さらに重力の向きへ半径分先までのレイを作る。
    /// 進む向きだけにレイを飛ばすと、床の上を水平に滑る粒が足元の面を見失い、少しずつ沈んで面をすり抜ける為、
    /// 常に重力の向きへ半径分を足す。
    /// </summary>
    [BurstCompile]
    public struct VoxelParticleRaycastBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VoxelMeltParticle> Particles;
        public float DeltaTime;
        public float3 Gravity;
        public QueryParameters QueryParameters;
        [WriteOnly] public NativeArray<RaycastCommand> Commands;

        public void Execute(int index)
        {
            var particle = Particles[index];
            var down = math.lengthsq(Gravity) > 1e-12f ? math.normalize(Gravity) : new float3(0f, -1f, 0f);
            var displacement = (particle.Velocity + Gravity * DeltaTime) * DeltaTime;
            var probe = displacement + down * particle.Radius;

            var length = math.length(probe);
            var direction = length > 1e-6f ? probe / length : down;

            Commands[index] = new RaycastCommand(particle.Position, direction, QueryParameters,
                math.max(length, particle.Radius));
        }
    }

    /// <summary>
    /// レイが当たった粒を面の外へ戻し、速度を更新する。
    /// </summary>
    [BurstCompile]
    public struct VoxelParticleResolveHitJob : IJobParallelFor
    {
        public NativeArray<VoxelMeltParticle> Particles;
        [ReadOnly] public NativeArray<RaycastHit> Hits;
        public float DeltaTime;
        public float HotDamping;
        public float ColdDamping;
        public float FreezeTemperature;
        public float EvaporationTemperature;

        public void Execute(int index)
        {
            // 当たらなかったレイの結果は、当たったコライダーが無い状態になる
            if (Hits[index].colliderEntityId == EntityId.None) return;

            var particle = Particles[index];
            if (particle.IsFrozen) return;

            float3 normal = Hits[index].normal;
            particle.Position = (float3)Hits[index].point + normal * particle.Radius;
            VoxelParticleContact.Apply(ref particle, normal, DeltaTime, HotDamping, ColdDamping, FreezeTemperature,
                EvaporationTemperature);
            Particles[index] = particle;
        }
    }

    /// <summary>
    /// 1 つの VoxelPiece の SDF と粒の当たりを取り、表面から粒の半径より内側にある粒を外へ押し出す。
    /// コライダーではなく SDF を直接読む為、溶けた直後でコライダーの更新が追いついていなくても正しく当たる。
    /// </summary>
    [BurstCompile]
    public struct VoxelParticleCollideJob : IJobParallelFor
    {
        public NativeArray<VoxelMeltParticle> Particles;
        [ReadOnly] public NativeArray<float> Samples;
        public VoxelGridLayout Layout;
        public float4x4 WorldToLocal;
        public float4x4 LocalToWorld;

        /// <summary> ローカル空間の長さをワールド空間の長さにする倍率 </summary>
        public float Scale;

        public float DeltaTime;
        public float HotDamping;
        public float ColdDamping;
        public float FreezeTemperature;
        public float EvaporationTemperature;

        public void Execute(int index)
        {
            var particle = Particles[index];
            if (particle.IsFrozen) return;

            var local = math.transform(WorldToLocal, particle.Position);
            if (!VoxelSampling.TryGetGridPosition(Layout, local, out var grid)) return;

            var radius = particle.Radius;
            var distance = VoxelSampling.Trilinear(Samples, Layout, grid) * Scale;
            if (distance >= radius) return;

            var gradient = VoxelSampling.Gradient(Samples, Layout, grid);
            if (math.lengthsq(gradient) < 1e-12f) return;

            var normal = math.normalize(math.rotate(LocalToWorld, gradient));
            particle.Position += normal * (radius - distance);
            VoxelParticleContact.Apply(ref particle, normal, DeltaTime, HotDamping, ColdDamping, FreezeTemperature,
                EvaporationTemperature);
            Particles[index] = particle;
        }
    }

    /// <summary>
    /// 形状の内側にある粒を加熱する。凝固点以上に戻った粒は、また動くようにする。
    ///
    /// 具象の形状ごとに [assembly: RegisterGenericJobType(typeof(VoxelParticleHeatJob&lt;形状&gt;))] で登録すること。
    /// </summary>
    [BurstCompile]
    public struct VoxelParticleHeatJob<TShape> : IJobParallelFor where TShape : struct, IVoxelShape
    {
        public NativeArray<VoxelMeltParticle> Particles;
        public TShape Shape;
        public float Amount;
        public float Falloff;
        public float FreezeTemperature;

        public void Execute(int index)
        {
            var particle = Particles[index];
            var distance = Shape.Distance(particle.Position);
            if (distance >= 0f) return;

            var weight = Falloff > 0f ? math.saturate(-distance / Falloff) : 1f;
            particle.Temperature = math.max(particle.Temperature + Amount * weight, 0f);
            if (particle.Temperature >= FreezeTemperature) particle.IsFrozen = false;
            Particles[index] = particle;
        }
    }
}
