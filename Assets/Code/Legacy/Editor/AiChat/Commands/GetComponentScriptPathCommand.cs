// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class GetComponentScriptPathCommand : IAiCommand
    {
        public string Name => "GetComponentScriptPath";
        public string Description => "GetComponentScriptPath [Path] [Component]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 2)
                return "Error: Missing arguments. Expected 2 (Path, Component).";

            var go = GameObjectResolver.Resolve(arguments[0]);
            if (go == null) return $"Error: GameObject not found at {arguments[0]}.";

            var comp = go.GetComponent(arguments[1]);
            if (comp == null) return $"Error: Component {arguments[1]} not found.";

            // MonoBehaviourの場合、MonoScriptからパスを取得可能
            if (comp is MonoBehaviour mb)
            {
                MonoScript ms = MonoScript.FromMonoBehaviour(mb);
                if (ms != null)
                {
                    string path = AssetDatabase.GetAssetPath(ms);
                    return $"Script path for {arguments[1]}: {path}";
                }
            }

            // ScriptableObject等の場合（Transformなどの組み込みコンポーネント以外）
            var type = comp.GetType();
            var scripts = AssetDatabase.FindAssets($"t:MonoScript {type.Name}");
            foreach (var guid in scripts)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type)
                {
                    return $"Script path for {arguments[1]}: {path}";
                }
            }

            return $"Info: Component {arguments[1]} is likely a built-in Unity component (no source script found).";
        }
    }
}
#endif
