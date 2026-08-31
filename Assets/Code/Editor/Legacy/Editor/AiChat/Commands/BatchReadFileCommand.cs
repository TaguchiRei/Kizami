// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class BatchReadFileCommand : IAiCommand
    {
        public string Name => "BatchReadFile";
        public string Description => "複数のファイルを一括で読み込みます。引数: [Path1] [Path2] ...";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing arguments [Paths...]";

            StringBuilder sb = new StringBuilder();
            foreach (var path in arguments)
            {
                if (File.Exists(path))
                {
                    sb.AppendLine($"--- File: {path} ---");
                    sb.AppendLine(File.ReadAllText(path));
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine($"--- File Error: {path} (Not Found) ---");
                }
            }

            return sb.ToString();
        }
    }
}
#endif
