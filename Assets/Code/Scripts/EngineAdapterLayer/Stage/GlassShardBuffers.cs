using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// GlassShardPool の全破片の動きの状態。破片は番号 グループの番号 × 1 グループの破片数 + 破片の番号 で 1 本の配列に並べ、Job でまとめて進める。
    /// グループごとの値（残り時間・地面の高さ）はグループの番号で引く。残り時間が 0 以下のグループは動かさない。
    /// </summary>
    public sealed class GlassShardBuffers : IDisposable
    {
        /// <summary> 破片ごとの速度（m/s、ワールド空間） </summary>
        public NativeArray<float3> Velocities;

        /// <summary> 破片ごとの角速度（rad/s） </summary>
        public NativeArray<float3> AngularVelocities;

        /// <summary> 破片ごとの、飛ばし始めたときの大きさ。寿命の終わりに縮めるときの元になる </summary>
        public NativeArray<float3> SpawnScales;

        /// <summary> 破片ごとの、地面に着いて止まったか </summary>
        public NativeArray<bool> IsLanded;

        /// <summary> グループごとの、消すまでの残り時間（秒）。0 以下は動かしていない </summary>
        public NativeArray<float> Remaining;

        /// <summary> グループごとの、破片が止まる地面の高さ（ワールド空間） </summary>
        public NativeArray<float> GroundY;

        /// <summary> 1 グループの破片の数 </summary>
        public readonly int ShardsPerGroup;

        public GlassShardBuffers(int groupCount, int shardsPerGroup)
        {
            ShardsPerGroup = shardsPerGroup;
            var shardCount = groupCount * shardsPerGroup;
            Velocities = new NativeArray<float3>(shardCount, Allocator.Persistent);
            AngularVelocities = new NativeArray<float3>(shardCount, Allocator.Persistent);
            SpawnScales = new NativeArray<float3>(shardCount, Allocator.Persistent);
            IsLanded = new NativeArray<bool>(shardCount, Allocator.Persistent);
            Remaining = new NativeArray<float>(groupCount, Allocator.Persistent);
            GroundY = new NativeArray<float>(groupCount, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (Velocities.IsCreated) Velocities.Dispose();
            if (AngularVelocities.IsCreated) AngularVelocities.Dispose();
            if (SpawnScales.IsCreated) SpawnScales.Dispose();
            if (IsLanded.IsCreated) IsLanded.Dispose();
            if (Remaining.IsCreated) Remaining.Dispose();
            if (GroundY.IsCreated) GroundY.Dispose();
        }
    }
}
