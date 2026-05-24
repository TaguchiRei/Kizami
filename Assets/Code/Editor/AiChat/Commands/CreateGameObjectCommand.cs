using System.Collections.Generic;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class CreateGameObjectCommand : IAiCommand
    {
        public string Name => "CreateGameObject";
        public string Description => "CreateGameObject [Name] [ParentName (optional)]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing Name.";
            
            var go = new GameObject(arguments[0]);
            if (arguments.Count > 1)
            {
                var parent = GameObject.Find(arguments[1]);
                if (parent != null) go.transform.SetParent(parent.transform);
            }
            return $"Created: {go.name}";
        }
    }
}