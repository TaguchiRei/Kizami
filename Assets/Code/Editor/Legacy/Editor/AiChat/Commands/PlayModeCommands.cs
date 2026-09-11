// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class EnterPlayModeCommand : IAiCommand
    {
        public string Name => "EnterPlayMode";
        public string Description => "Enter Unity Play Mode.";
        public string Execute(List<string> arguments)
        {
            EditorApplication.isPlaying = true;
            return "Entering Play Mode...";
        }
    }

    public class ExitPlayModeCommand : IAiCommand
    {
        public string Name => "ExitPlayMode";
        public string Description => "Exit Unity Play Mode.";
        public string Execute(List<string> arguments)
        {
            EditorApplication.isPlaying = false;
            return "Exiting Play Mode...";
        }
    }

    public class IsPlayingCommand : IAiCommand
    {
        public string Name => "IsPlaying";
        public string Description => "Check if Unity is in Play Mode.";
        public string Execute(List<string> arguments)
        {
            return EditorApplication.isPlaying ? "True" : "False";
        }
    }
}
#endif
