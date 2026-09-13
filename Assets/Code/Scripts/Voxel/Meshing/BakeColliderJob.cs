using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Kizami.Voxel
{
    /// <summary>
    /// MeshCollider 用の物理メッシュを、ワーカースレッドで事前にベイクする。
    /// CookingOptions は割り当て先の MeshCollider.cookingOptions と一致させること。
    /// 一致しないと、割り当て時にメインスレッドでベイクし直される。
    /// </summary>
    public struct BakeColliderJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<EntityId> MeshIds;
        public MeshColliderCookingOptions CookingOptions;

        public void Execute(int index)
        {
            Physics.BakeMesh(MeshIds[index], false, CookingOptions);
        }
    }
}
