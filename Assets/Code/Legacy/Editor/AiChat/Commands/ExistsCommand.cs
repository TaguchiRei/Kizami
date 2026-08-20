// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class ExistsCommand : IAiCommand
    {
        public string Name => "Exists";
        public string Description => "Exists [Path]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count == 0) return "Error: Missing Path.";
            string path = arguments[0];
            return (File.Exists(path) || Directory.Exists(path)).ToString();
        }
    }
}
#endif
