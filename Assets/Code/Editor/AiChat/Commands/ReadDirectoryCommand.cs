using System.Collections.Generic;
using System.IO;

namespace UsefulTools.Editor.Ai.Commands
{
    public class ReadDirectoryCommand : IAiCommand
    {
        public string Name => "ReadDirectory";
        public string Description => "ReadDirectory [Path]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 1)
                return "Error: Missing argument. Expected [Path].";

            string path = arguments[0];
            if (!FileSecurity.IsAccessAllowed(path))
                return FileSecurity.GetSecurityError(path);

            if (!Directory.Exists(path))
                return "Error: Directory not found.";

            return string.Join("\n", Directory.GetFileSystemEntries(path));
        }
    }
}