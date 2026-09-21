using UnityEngine;

namespace Kizami.EngineAdapter.Voxel
{
    /// <summary>
    /// 加熱・融解・蒸発・冷却と、融解した粒の動きに関わる設定。
    /// 温度は、常温を 0、融点を 1 とした値。
    /// </summary>
    [CreateAssetMenu(menuName = "Kizami/Voxel/Thermal Settings", fileName = "VoxelThermalSettings")]
    public sealed class VoxelThermalSettings : ScriptableObject
    {
        [Header("温度")]
        [SerializeField, Min(1f)]
        [Tooltip("この温度以上で蒸発する。固体がこの温度以上で融解したときは、粒にならずに直接蒸発する")]
        private float _evaporationTemperature = 2f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("粒がこの温度より冷えると、固まって止まる。融点（1）より低くしないと、溶けた直後に固まる")]
        private float _freezeTemperature = 0.8f;

        [SerializeField, Min(0f)]
        [Tooltip("固体のサンプルが 1 秒あたりに下がる温度")]
        private float _solidCoolingPerSecond = 0.1f;

        [SerializeField, Min(0f)]
        [Tooltip("融解した粒が 1 秒あたりに下がる温度")]
        private float _particleCoolingPerSecond = 0.1f;

        [Header("粒")]
        [SerializeField, Range(1, 4)]
        [Tooltip("融解した部分を粒にまとめるときの、粒 1 個あたりの各軸のサンプル数。大きいほど粒が少なく大きくなる")]
        private int _particleCoarseness = 1;

        [SerializeField]
        [Tooltip("粒にかかる重力の倍率")]
        private float _gravityScale = 1f;

        [SerializeField, Min(0.01f)]
        [Tooltip("粒の速さの上限（m/s）。1 フレームの移動がボクセル数個分より大きくなると、面をすり抜ける")]
        private float _maxSpeed = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("蒸発点のときの、面に沿う速度の 1 秒あたりの減り方。小さいほどよく流れる")]
        private float _hotTangentDamping = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("凝固点のときの、面に沿う速度の 1 秒あたりの減り方。大きいほど面に貼り付く")]
        private float _coldTangentDamping = 12f;

        [Header("押し広げ")]
        [SerializeField, Min(0f)]
        [Tooltip("粒が密集している所から押し広げる速さ（m/s）。充填率が基準を 1 上回ったときの値。0 なら押し広げない")]
        private float _spreadSpeed = 0.5f;

        [SerializeField, Range(0.1f, 2f)]
        [Tooltip("押し広げを始める充填率。粒の直径ほどのセルに、粒が 1 個入っていると約 0.52")]
        private float _spreadRestFill = 0.6f;

        public float EvaporationTemperature => _evaporationTemperature;
        public float FreezeTemperature => _freezeTemperature;
        public float SolidCoolingPerSecond => _solidCoolingPerSecond;
        public float ParticleCoolingPerSecond => _particleCoolingPerSecond;
        public int ParticleCoarseness => _particleCoarseness;
        public float GravityScale => _gravityScale;
        public float MaxSpeed => _maxSpeed;
        public float HotTangentDamping => _hotTangentDamping;
        public float ColdTangentDamping => _coldTangentDamping;
        public float SpreadSpeed => _spreadSpeed;
        public float SpreadRestFill => _spreadRestFill;
    }
}
