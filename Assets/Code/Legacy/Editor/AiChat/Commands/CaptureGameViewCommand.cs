// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class CaptureGameViewCommand : IAiCommand
    {
        public string Name => "CaptureGameView";
        public string Description => "Capture the current GameView screenshot.";

        public string Execute(List<string> arguments)
        {
            AiGameViewCapture.Capture();
            return "GameView captured successfully. The image will be sent to the AI in the next turn.";
        }
    }
}
#endif
