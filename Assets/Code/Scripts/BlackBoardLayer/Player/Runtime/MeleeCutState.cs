using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 近接切断の切断面の角度を保持するステート。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class MeleeCutState : SceneStateBase, IMeleeCutState
    {
        public float Angle { get; private set; }

        /// <summary>
        /// 切断面の角度を設定する。
        /// </summary>
        /// <param name="angle">切断面の角度（度）。0 以上 180 未満</param>
        public void SetAngle(float angle)
        {
            Angle = angle;
        }

        public override string GetLog()
        {
            return $"Angle: {Angle}";
        }
    }

    /// <summary>
    /// 近接切断の切断面の角度の読み取り面。
    /// </summary>
    public interface IMeleeCutState : IStateGetter
    {
        /// <summary>
        /// 切断面の角度（度）。0 以上 180 未満。
        /// 0 で水平に切り、値が増えると画面上で反時計回りに傾く。
        /// </summary>
        float Angle { get; }
    }
}
