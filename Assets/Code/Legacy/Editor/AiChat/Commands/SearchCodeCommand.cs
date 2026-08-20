// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class SearchCodeCommand : IAiCommand
    {
        public string Name => "SearchCode";
        public string Description => "プロジェクト内の全スクリプトからキーワードを検索します。引数: [Keyword] [Optional: Directory(default:Assets)]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing argument [Keyword]";

            string keyword = arguments[0];
            string searchDir = arguments.Count > 1 ? arguments[1] : "Assets";

            if (!Directory.Exists(searchDir)) return $"Error: Directory not found: {searchDir}";

            var files = Directory.GetFiles(searchDir, "*.cs", SearchOption.AllDirectories);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Search results for '{keyword}' in {searchDir}:");

            int matchCount = 0;
            const int MaxMatches = 50;

            foreach (var file in files)
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(keyword))
                    {
                        builder.AppendLine($"{file} (Line {i + 1}): {lines[i].Trim()}");
                        matchCount++;
                        if (matchCount >= MaxMatches)
                        {
                            builder.AppendLine("... and more matches (truncated).");
                            return builder.ToString();
                        }
                    }
                }
            }

            if (matchCount == 0) return $"No matches found for '{keyword}'.";

            return builder.ToString();
        }
    }
}
#endif
