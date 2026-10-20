using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 体力制の破壊対象の体力・名前・表示の状態を保持するステート。1つのステージに1つだけ持つ。対象がないステージでは最大値が0のまま。
    /// </summary>
    [RegisterBoard(typeof(StageBoard))]
    public sealed class TargetHealthState : SceneStateBase, ITargetHealthState
    {
        public string Name { get; private set; } = string.Empty;
        public float Current { get; private set; }
        public float Max { get; private set; }
        public TargetHealthMode Mode { get; private set; } = TargetHealthMode.Normal;

        public override string GetLog()
        {
            return $"Target: {Name}  \nHealth: {Current} / {Max}  \nMode: {Mode}";
        }

        /// <summary>
        /// 名前と最大体力を設定し、体力を満タンにする。
        /// </summary>
        /// <param name="name">ゲージに出す名前</param>
        /// <param name="max">最大体力</param>
        public void Setup(string name, float max)
        {
            Name = name;
            Max = max;
            Current = max;
        }

        /// <summary>
        /// 現在の体力を設定する。
        /// </summary>
        /// <param name="current">現在の体力。0〜最大体力に収める</param>
        public void SetCurrent(float current)
        {
            Current = UnityEngine.Mathf.Clamp(current, 0f, Max);
        }

        /// <summary>
        /// 表示の状態を設定する。
        /// </summary>
        /// <param name="mode">表示の状態</param>
        public void SetMode(TargetHealthMode mode)
        {
            Mode = mode;
        }
    }

    /// <summary>
    /// 体力制の破壊対象の体力の読み取り面。
    /// </summary>
    public interface ITargetHealthState : IStateGetter
    {
        /// <summary> ゲージに出す名前 </summary>
        string Name { get; }

        /// <summary> 現在の体力 </summary>
        float Current { get; }

        /// <summary> 最大体力。対象がないステージでは0 </summary>
        float Max { get; }

        /// <summary> 表示の状態 </summary>
        TargetHealthMode Mode { get; }
    }

    /// <summary>
    /// 体力のゲージの表示の状態。
    /// </summary>
    public enum TargetHealthMode
    {
        /// <summary> 通常 </summary>
        Normal,

        /// <summary> 無敵。体力が減らない </summary>
        Invincible,

        /// <summary> 時間で減っていく。ゲージを灰色にして赤く点滅させる </summary>
        Draining,

        /// <summary> 壊れて止まった。ゲージを灰色にして、ひびを入れる </summary>
        Broken,
    }
}
