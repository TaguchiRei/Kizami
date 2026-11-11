namespace Kizami.BlackBoard
{
    /// <summary>
    /// プレイヤーにかける重力の強さと、ジャンプの軌道に付ける重力の倍率。
    /// 値の正本は PlayerParameterData で、Initializer が組み立てて移動の Adapter へ渡す。プレイヤー以外の物理の重力（Physics.gravity）とは別に持つ。
    /// </summary>
    public readonly struct PlayerGravity
    {
        /// <summary> 重力の強さ（m/s^2）。倍率 1 のときの、下向きの加速度の大きさ </summary>
        public readonly float Strength;

        /// <summary> 上昇中の倍率 </summary>
        public readonly float RiseScale;

        /// <summary> ジャンプボタンの長押し中の、上昇中の倍率 </summary>
        public readonly float HoldRiseScale;

        /// <summary> 最高点の前後（縦の速さが ApexSpeedThreshold より小さいとき）の倍率 </summary>
        public readonly float ApexScale;

        /// <summary> 最高点の前後とみなす、縦の速さの閾値（m/s） </summary>
        public readonly float ApexSpeedThreshold;

        /// <summary> 下降中の倍率 </summary>
        public readonly float FallScale;

        /// <summary> 最高点の前後の倍率から下降中の倍率へ移るまでの、落下の速さの幅（m/s） </summary>
        public readonly float FallRampSpeed;

        public PlayerGravity(float strength, float riseScale, float holdRiseScale, float apexScale,
            float apexSpeedThreshold, float fallScale, float fallRampSpeed)
        {
            Strength = strength;
            RiseScale = riseScale;
            HoldRiseScale = holdRiseScale;
            ApexScale = apexScale;
            ApexSpeedThreshold = apexSpeedThreshold;
            FallScale = fallScale;
            FallRampSpeed = fallRampSpeed;
        }
    }
}
