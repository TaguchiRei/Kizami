using System.Collections.Generic;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class CopyFileCommand : IAiCommand
    {
        public string Name => "CopyFile";
        public string Description => "CopyFile [OldPath] [NewPath]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 2)
                return "Error: Missing arguments. Expected 2 (OldPath, NewPath).";

            string oldPath = arguments[0];
            string newPath = arguments[1];

            if (!FileSecurity.IsAccessAllowed(oldPath) || !FileSecurity.IsAccessAllowed(newPath))
                return "Error: Access denied. Both paths must be within Assets.";

            bool success = AssetDatabase.CopyAsset(oldPath, newPath);
            AssetDatabase.Refresh();
            
            return success ? "Success" : "Failed to copy.";
        }
    }
}