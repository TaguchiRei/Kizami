using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class GetScriptsOnObjectCommand : IAiCommand
    {
        public string Name => "GetScriptsOnObject";
        public string Description => "指定したGameObjectに付いている全MonoBehaviourのスクリプトパスを取得します。引数: [Path or #ID]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing argument [Path or #ID]";

            GameObject target = GameObjectResolver.Resolve(arguments[0]);
            if (target == null) return $"Error: GameObject not found: {arguments[0]}";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Scripts on '{target.name}':");

            foreach (var comp in target.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                MonoScript ms = MonoScript.FromMonoBehaviour(comp);
                if (ms != null)
                {
                    string path = AssetDatabase.GetAssetPath(ms);
                    sb.AppendLine($"- {comp.GetType().Name}: {path}");
                }
            }

            return sb.ToString();
        }
    }
}
