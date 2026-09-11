// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class DestroyGameObjectCommand : IAiCommand
    {
        public string Name => "DestroyGameObject";
        public string Description => "シーン内のGameObjectを削除します。引数: [Path or #ID]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing identifier (Path or #ID).";
            
            var go = GameObjectResolver.Resolve(arguments[0]);
            if (go == null) return $"Error: GameObject '{arguments[0]}' not found or already destroyed.";
            
            string name = go.name;
            UnityEditor.Undo.DestroyObjectImmediate(go);
            return $"Destroyed GameObject: {name}";
        }
    }
}
#endif
