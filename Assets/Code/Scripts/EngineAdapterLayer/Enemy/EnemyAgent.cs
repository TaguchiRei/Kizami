using Unity.Mathematics;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵 1 体の状態。EnemySpawnAdapter が NativeArray に持ち、Burst の Job で読み書きする。
    /// 後から ECS のコンポーネントへ移せるよう、値型のフィールドだけで持つ。
    /// </summary>
    public struct EnemyAgent
    {
        /// <summary> ステージに出ているか </summary>
        public bool IsAlive;

        /// <summary> 体の根の位置 </summary>
        public float3 Position;

        /// <summary> 向き（Y 軸まわりの角度、ラジアン）。0 で +Z を向く </summary>
        public float Yaw;
    }
}
