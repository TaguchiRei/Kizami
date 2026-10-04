using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 近接切断の出来事を流すチャンネルの組。
    /// </summary>
    public sealed class MeleeCutEvents : IMeleeCutEvents
    {
        private readonly ActionChannel<float> _onSwing = new();

        public IActionChannel<float> OnSwing => _onSwing;

        /// <summary>
        /// 剣を振ったことを流す。
        /// </summary>
        /// <param name="angle">振ったときの切断面の角度（度）</param>
        public void RaiseSwing(float angle)
        {
            _onSwing.Invoke(angle);
        }
    }

    /// <summary>
    /// 近接切断の出来事の購読面。
    /// </summary>
    public interface IMeleeCutEvents : IEvent
    {
        /// <summary>
        /// 剣を振ったときに流れる。攻撃間隔より短い間隔の攻撃入力では流れない。
        /// 引数は振ったときの切断面の角度（度。IMeleeCutState.Angle と同じ向き）
        /// </summary>
        IActionChannel<float> OnSwing { get; }
    }
}
