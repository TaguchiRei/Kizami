using System.Collections.Generic;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class DeleteGameObjectCommand : IAiCommand
    {
        public string Name => "DeleteGameObject";
        public string Description => "DeleteGameObject [Name]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing Name.";
            
            var go = GameObject.Find(arguments[0]);
            if (go == null) return $"Error: GameObject '{arguments[0]}' not found or already destroyed.";
            
            Object.DestroyImmediate(go);
            return $"Deleted: {arguments[0]}";
        }
    }
}