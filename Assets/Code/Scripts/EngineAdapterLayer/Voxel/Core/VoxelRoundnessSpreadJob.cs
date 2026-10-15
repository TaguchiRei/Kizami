using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// サンプル範囲 [RangeMin, RangeMax] の中で、点ごとの丸み（0〜255）を周りへ薄めて広げる。
    /// 隣の点の丸みから Step を引いた値の方が大きければ置き換える処理を、軸ごとに往復して行う為、減り方はセル数のマンハッタン距離に比例する。
    /// 最大値で広げるので、範囲の外の値を足場にせず、何度計算し直しても結果は変わらない。
    /// 範囲は、新しく印を付けた範囲を広がる幅だけ広げたものにする。
    /// </summary>
    [BurstCompile]
    public struct VoxelRoundnessSpreadJob : IJob
    {
        public VoxelGridLayout Layout;
        public int3 RangeMin;
        public int3 RangeMax;

        /// <summary> 1 セル離れるごとに減らす丸み </summary>
        public float Step;

        public NativeArray<byte> Roundness;

        private void Spread(int axis)
        {
            var axis1 = (axis + 1) % 3;
            var axis2 = (axis + 2) % 3;

            for (var b = RangeMin[axis2]; b <= RangeMax[axis2]; b++)
            for (var a = RangeMin[axis1]; a <= RangeMax[axis1]; a++)
            {
                var sample = int3.zero;
                sample[axis1] = a;
                sample[axis2] = b;

                var carried = 0f;
                for (var i = RangeMin[axis]; i <= RangeMax[axis]; i++)
                {
                    sample[axis] = i;
                    carried = Carry(Layout.ToSampleIndex(sample), carried);
                }

                carried = 0f;
                for (var i = RangeMax[axis]; i >= RangeMin[axis]; i--)
                {
                    sample[axis] = i;
                    carried = Carry(Layout.ToSampleIndex(sample), carried);
                }
            }
        }

        private float Carry(int index, float carried)
        {
            carried = math.max(Roundness[index], carried - Step);
            Roundness[index] = (byte)carried;
            return carried;
        }

        public void Execute()
        {
            for (var axis = 0; axis < 3; axis++)
            {
                Spread(axis);
            }
        }
    }
}
