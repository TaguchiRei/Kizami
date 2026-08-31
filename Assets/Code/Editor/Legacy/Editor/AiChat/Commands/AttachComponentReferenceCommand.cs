// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class AttachComponentReferenceCommand : IAiCommand
    {
        public string Name => "AttachComponentReference";
        public string Description => "AttachComponentReference [GameObjectName] [ComponentName] [FieldName] [TargetGameObjectName]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 4) return "Error: Missing arguments (GameObjectName, ComponentName, FieldName, TargetPathOrName).";

            var go = GameObjectResolver.Resolve(arguments[0]);
            if (go == null) return "Error: GameObject not found.";

            var comp = go.GetComponent(arguments[1]);
            if (comp == null) return "Error: Component not found.";

            var so = new SerializedObject(comp);
            var prop = so.FindProperty(arguments[2]);
            if (prop == null) return "Error: Field not found.";

            // ターゲットを探す（まず名前で検索、見つからなければパスとしてロード）
            UnityEngine.Object target = GameObjectResolver.Resolve(arguments[3]);
            if (target == null) target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(arguments[3]);

            if (target == null) return $"Error: Target '{arguments[3]}' not found as GameObject or Asset.";

            prop.objectReferenceValue = target;
            so.ApplyModifiedProperties();
            
            return $"Attached {target.name} to {comp.GetType().Name}.{arguments[2]}";
        }
    }
}
#endif
