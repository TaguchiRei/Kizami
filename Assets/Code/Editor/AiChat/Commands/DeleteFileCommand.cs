using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class DeleteFileCommand : IAiCommand
    {
        public string Name => "DeleteFile";
        public string Description => "DeleteFile [Path]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 1)
                return "Error: Missing argument. Expected [Path].";

            string path = arguments[0];
            if (!FileSecurity.IsAccessAllowed(path))
                return FileSecurity.GetSecurityError(path);

            if (File.Exists(path))
            {
                File.Delete(path);
                AssetDatabase.Refresh();
                return $"Deleted file: {path}";
            }
            
            return "Error: File not found.";
        }
    }
}