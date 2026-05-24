using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class WriteFileCommand : IAiCommand
    {
        public string Name => "WriteFile";
        public string Description => "WriteFile [Path] [Content]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 2)
                return "Error: Missing arguments. Expected 2 (Path, Content).";

            string path = arguments[0];
            string content = arguments[1];

            if (!FileSecurity.IsAccessAllowed(path))
                return FileSecurity.GetSecurityError(path);

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            
            File.WriteAllText(path, content);
            AssetDatabase.Refresh();
            
            return $"Created file: {path}";
        }
    }
}