// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class FindAssetsCommand : IAiCommand
    {
        public string Name => "FindAssets";
        public string Description => "AssetDatabaseを使用してアセットを検索します。引数: [Filter or Name]。接頭辞(t:, l:等)がない場合は名前検索として動作します。";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count == 0) return "Error: Missing Filter or Name.";
            
            string filter = arguments[0];
            // フィルタに ":" が含まれていない場合は、名前検索として扱うために "n:" を付与することを検討
            // ただし AssetDatabase.FindAssets は引数がそのまま名前検索としても機能するため、
            // そのまま渡すのが最も汎用的。
            
            var guids = AssetDatabase.FindAssets(filter);
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
            
            if (paths.Count == 0) return $"No assets found for '{filter}'.";

            string result = $"Found {paths.Count} asset(s) for '{filter}':\n";
            result += string.Join("\n", paths.Take(50));
            if (paths.Count > 50) result += "\n... (truncated)";
            
            return result;
        }
    }
}
#endif
