// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class PatchFileCommand : IAiCommand
    {
        public string Name => "PatchFile";
        public string Description => "PatchFile [Path] [OldContent] [NewContent]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 3)
                return "Error: Missing arguments. Expected 3 (Path, OldContent, NewContent).";

            string path = arguments[0];
            string oldContent = arguments[1];
            string newContent = arguments[2];

            if (!FileSecurity.IsAccessAllowed(path))
                return FileSecurity.GetSecurityError(path);

            if (!File.Exists(path))
                return $"Error: File not found at {path}";

            string fileContent = File.ReadAllText(path);
            
            // 安全な置換を行う（全一致置換ではなく、最初の1箇所のみを置換）
            int index = fileContent.IndexOf(oldContent);
            if (index == -1)
                return "Error: Could not find the specified content to patch.";

            string patchedContent = fileContent.Remove(index, oldContent.Length).Insert(index, newContent);

            File.WriteAllText(path, patchedContent);
            AssetDatabase.Refresh();
            
            return $"Successfully patched: {path}";
        }
    }
}
#endif
