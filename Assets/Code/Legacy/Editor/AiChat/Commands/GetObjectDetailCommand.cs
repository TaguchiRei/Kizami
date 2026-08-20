// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class GetObjectDetailCommand : IAiCommand
    {
        public string Name => "GetObjectDetail";
        public string Description => "指定したGameObjectの詳細情報（Transform, Components, Inspector要約）を一括取得します。引数: [Path or #ID]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing argument [Path or #ID]";

            GameObject target = GameObjectResolver.Resolve(arguments[0]);
            if (target == null) return $"Error: GameObject not found: {arguments[0]}";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"--- Detail for '{target.name}' (# {target.GetInstanceID()}) ---");

            // 1. Transform Info
            Transform t = target.transform;
            sb.AppendLine("[Transform]");
            sb.AppendLine($"  Pos: {t.localPosition}, Rot: {t.localEulerAngles}, Scale: {t.localScale}");
            sb.AppendLine($"  World Pos: {t.position}");
            sb.AppendLine($"  Parent: {(t.parent != null ? t.parent.name : "None")}, Children: {t.childCount}");

            // 2. Components & Simple Inspector Summary
            sb.AppendLine("\n[Components]");
            foreach (var comp in target.GetComponents<Component>())
            {
                if (comp == null)
                {
                    sb.AppendLine("  - Missing Script");
                    continue;
                }

                string typeName = comp.GetType().Name;
                sb.Append($"  - {typeName} (#{comp.GetInstanceID()})");

                // 主要なコンポーネントのみ、値を少し出す
                if (comp is Camera cam) sb.Append($" (FOV: {cam.fieldOfView}, Near: {cam.nearClipPlane}, Far: {cam.farClipPlane})");
                else if (comp is Light light) sb.Append($" (Type: {light.type}, Intensity: {light.intensity}, Color: {light.color})");
                else if (comp is MeshFilter mf) sb.Append($" (Mesh: {(mf.sharedMesh != null ? mf.sharedMesh.name : "None")})");
                
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
#endif
