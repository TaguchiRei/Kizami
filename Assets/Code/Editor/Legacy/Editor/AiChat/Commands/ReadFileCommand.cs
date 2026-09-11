// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.IO;

namespace UsefulTools.Editor.Ai.Commands
{
    public class ReadFileCommand : IAiCommand
    {
        public string Name => "ReadFile";
        public string Description => "ReadFile [Path]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 1)
                return "Error: Missing argument. Expected [Path].";

            string path = arguments[0];
            if (!FileSecurity.IsAccessAllowed(path))
                return FileSecurity.GetSecurityError(path);
                
            if (!File.Exists(path))
                return $"Error: File not found at {path}";

            return File.ReadAllText(path);
        }
    }
}
#endif
