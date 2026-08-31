// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class CreateDirectoryCommand : IAiCommand
    {
        public string Name => "CreateDirectory";
        public string Description => "CreateDirectory [Path]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 1)
                return "Error: Missing argument. Expected [Path].";

            string path = arguments[0];
            if (!FileSecurity.IsAccessAllowed(path))
                return FileSecurity.GetSecurityError(path);

            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
            
            return $"Created directory: {path}";
        }
    }
}
#endif
