using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// ビルドモードを保持するゲームステート。
    /// </summary>
    [RegisterBoard(typeof(AppBoard))]
    public class BuildModeState : GameStateBase, IBuildModeState
    {
        public BuildMode BuildMode { get; private set; }

        public void SetBuildMode(BuildMode mode)
        {
            BuildMode = mode;
        }

        public override string GetLog()
        {
            return BuildMode.ToString();
        }
    }

    public interface IBuildModeState : IStateGetter
    {
        BuildMode BuildMode { get; }
    }

    public enum BuildMode
    {
        PC,
        Mobile,
        VR
    }
}