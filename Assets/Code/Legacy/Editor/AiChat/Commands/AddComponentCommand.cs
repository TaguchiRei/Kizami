// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class AddComponentCommand : IAiCommand
    {
        public string Name => "AddComponent";
        public string Description => "AddComponent [Path] [Type]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 2)
                return "Error: Missing arguments. Expected 2 (Path, Type).";

            var go = GameObjectResolver.Resolve(arguments[0]);
            if (go == null) return $"Error: GameObject not found at path {arguments[0]}.";

            var type = GetSafeType(arguments[1]);
            if (type == null) return $"Error: Type {arguments[1]} not found.";

            Undo.AddComponent(go, type);
            return $"Added {type.Name} to {go.name}.";
        }

        private Type GetSafeType(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(typeName) ?? asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
                if (type != null) return type;
            }
            return null;
        }
    }
}
#endif
