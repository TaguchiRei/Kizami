// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class CreatePrefabCommand : IAiCommand
    {
        public string Name => "CreatePrefab";
        public string Description => "CreatePrefab [GameObjectName] [AssetPath]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 2) return "Error: Missing arguments. Expected 2 (GOName, Path).";
            
            var go = GameObjectResolver.Resolve(arguments[0]);
            if (go == null) return $"Error: GameObject {arguments[0]} not found.";

            string path = arguments[1];
            if (!path.EndsWith(".prefab")) path += ".prefab";

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            if (prefab != null)
            {
                return $"Prefab created successfully at {path}.";
            }
            return "Error: Failed to create prefab.";
        }
    }

    public class ApplyPrefabInstanceCommand : IAiCommand
    {
        public string Name => "ApplyPrefabInstance";
        public string Description => "ApplyPrefabInstance [GameObjectName]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing GameObjectName.";
            
            var go = GameObjectResolver.Resolve(arguments[0]);
            if (go == null) return $"Error: GameObject {arguments[0]} not found.";

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return $"Error: {arguments[0]} is not a prefab instance.";

            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.UserAction);
            return $"Prefab instance {arguments[0]} applied successfully.";
        }
    }
}
#endif
