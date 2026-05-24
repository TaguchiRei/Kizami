using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class FindAssetsCommand : IAiCommand
    {
        public string Name => "FindAssets";
        public string Description => "FindAssets [Filter]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count == 0) return "Error: Missing Filter.";
            var guids = AssetDatabase.FindAssets(arguments[0]);
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath).ToList();
            return paths.Count > 0 ? string.Join("\n", paths.Take(20)) : "No assets found.";
        }
    }
}