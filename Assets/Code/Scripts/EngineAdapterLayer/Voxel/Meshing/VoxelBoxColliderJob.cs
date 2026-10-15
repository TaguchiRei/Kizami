using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// サンプル範囲 [SampleMin, SampleMax) の中身の点（距離が負の点）を、隣どうしまとめた箱にする。
    /// まだ箱に入っていない中身の点から、x・y・z の順に、中身が続く限り伸ばす。
    /// 箱は両端の点を含むサンプル座標で出し、各点の ±半ボクセルを覆う。角ばった描き方の形と一致する。
    /// サンプル範囲は VoxelGridLayout.GetChunkSampleRange で決め、チャンクどうしで重ならないようにする。
    /// </summary>
    [BurstCompile]
    public struct VoxelBoxColliderJob : IJob
    {
        public VoxelGridLayout Layout;

        /// <summary> 箱にするサンプル範囲の最小（含む） </summary>
        public int3 SampleMin;

        /// <summary> 箱にするサンプル範囲の最大（含まない） </summary>
        public int3 SampleMax;

        [ReadOnly] public NativeArray<float> Samples;

        /// <summary> 箱の最小のサンプル座標（含む） </summary>
        public NativeList<int3> BoxMins;

        /// <summary> 箱の最大のサンプル座標（含む） </summary>
        public NativeList<int3> BoxMaxs;

        private static int ToLocalIndex(int3 local, int3 size)
        {
            return local.x + size.x * (local.y + size.y * local.z);
        }

        /// <summary>
        /// 中身の点で、まだ箱に入っていないか。
        /// </summary>
        private bool IsFree(int3 local, int3 size, NativeArray<bool> used)
        {
            return !used[ToLocalIndex(local, size)] && Samples[Layout.ToSampleIndex(SampleMin + local)] < 0f;
        }

        private bool IsRowFree(int xMin, int xMax, int y, int z, int3 size, NativeArray<bool> used)
        {
            for (var x = xMin; x <= xMax; x++)
            {
                if (!IsFree(new int3(x, y, z), size, used)) return false;
            }

            return true;
        }

        private bool IsSlabFree(int3 start, int3 end, int z, int3 size, NativeArray<bool> used)
        {
            for (var y = start.y; y <= end.y; y++)
            {
                if (!IsRowFree(start.x, end.x, y, z, size, used)) return false;
            }

            return true;
        }

        public void Execute()
        {
            var size = SampleMax - SampleMin;
            var used = new NativeArray<bool>(size.x * size.y * size.z, Allocator.Temp);

            for (var z = 0; z < size.z; z++)
            for (var y = 0; y < size.y; y++)
            for (var x = 0; x < size.x; x++)
            {
                var start = new int3(x, y, z);
                if (!IsFree(start, size, used)) continue;

                var end = start;
                while (end.x + 1 < size.x && IsFree(new int3(end.x + 1, y, z), size, used)) end.x++;

                while (end.y + 1 < size.y && IsRowFree(start.x, end.x, end.y + 1, z, size, used)) end.y++;

                while (end.z + 1 < size.z && IsSlabFree(start, end, end.z + 1, size, used)) end.z++;

                for (var bz = start.z; bz <= end.z; bz++)
                for (var by = start.y; by <= end.y; by++)
                for (var bx = start.x; bx <= end.x; bx++)
                {
                    used[ToLocalIndex(new int3(bx, by, bz), size)] = true;
                }

                BoxMins.Add(SampleMin + start);
                BoxMaxs.Add(SampleMin + end);
            }

            used.Dispose();
        }
    }
}
