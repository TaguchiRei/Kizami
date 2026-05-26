using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class DeleteAssetCommand : IAiCommand
    {
        public string Name => "DeleteAsset";
        public string Description => "プロジェクトからアセット（ファイルまたはフォルダ）を削除します。引数: [Path]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing path.";
            
            string path = arguments[0];
            if (!FileSecurity.IsAccessAllowed(path))
                return FileSecurity.GetSecurityError(path);

            if (!AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) && !Directory.Exists(path) && !File.Exists(path))
                return $"Error: Asset not found at {path}";

            if (AssetDatabase.DeleteAsset(path))
            {
                return $"Successfully deleted asset: {path}";
            }
            
            return $"Error: Failed to delete asset at {path}. It might be outside the Assets folder or locked.";
        }
    }
}