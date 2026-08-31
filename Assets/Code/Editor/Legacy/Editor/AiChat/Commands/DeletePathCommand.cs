// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class DeletePathCommand : IAiCommand
    {
        public string Name => "DeletePath";
        public string Description => "DeletePath [Path]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing path.";
            
            string path = arguments[0];
            if (!FileSecurity.IsAccessAllowed(path))
                return FileSecurity.GetSecurityError(path);

            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                return $"Deleted directory: {path}";
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
                return $"Deleted file: {path}";
            }
            
            return "Error: Path not found.";
        }
    }
}
#endif
