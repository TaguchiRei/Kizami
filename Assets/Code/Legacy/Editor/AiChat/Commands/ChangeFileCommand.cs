// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class ChangeFileCommand : IAiCommand
    {
        public string Name => "ChangeFile";
        public string Description => "ChangeFile [Path] [Content]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 2)
                return "Error: Missing arguments. Expected 2 (Path, Content).";

            string path = arguments[0];
            string newContent = arguments[1];

            if (!FileSecurity.IsAccessAllowed(path))
                return FileSecurity.GetSecurityError(path);

            if (!File.Exists(path))
                return $"Error: File not found at {path}";

            // 今後の課題：ここでは全置換しているが、将来的に
            // 差分パッチ(diff/patch)ロジックへ置き換える予定。
            // まずは安全なディレクトリ・ファイル存在確認を行う。
            
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            
            File.WriteAllText(path, newContent);
            AssetDatabase.Refresh();
            
            return $"Updated file: {path}";
        }
    }
}
#endif
