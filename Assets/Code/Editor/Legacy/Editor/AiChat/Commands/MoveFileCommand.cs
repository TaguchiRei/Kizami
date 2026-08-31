// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class MoveFileCommand : IAiCommand
    {
        public string Name => "MoveFile";
        public string Description => "MoveFile [OldPath] [NewPath]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 2)
                return "Error: Missing arguments. Expected 2 (OldPath, NewPath).";

            string oldPath = arguments[0];
            string newPath = arguments[1];

            if (!FileSecurity.IsAccessAllowed(oldPath) || !FileSecurity.IsAccessAllowed(newPath))
                return "Error: Access denied. Both paths must be within Assets.";

            string err = AssetDatabase.MoveAsset(oldPath, newPath);
            AssetDatabase.Refresh();
            
            return string.IsNullOrEmpty(err) ? "Success" : $"Error: {err}";
        }
    }
}
#endif
