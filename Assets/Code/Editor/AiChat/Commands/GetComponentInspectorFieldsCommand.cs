using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Text;
using System.Reflection;

namespace UsefulTools.Editor.Ai.Commands
{
    public class GetComponentInspectorFieldsCommand : IAiCommand
    {
        public string Name => "GetComponentInspectorFields";
        public string Description => "GetComponentInspectorFields [Path] [Component]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 2)
                return "Error: Missing arguments. Expected 2 (Path/ID, ComponentName).";

            var go = GameObjectResolver.Resolve(arguments[0]);
            if (go == null) return $"Error: GameObject not found at {arguments[0]}.";

            string searchName = arguments[1];
            Component targetComp = null;

            // 部分一致または完全一致でコンポーネントを探索
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;
                if (typeName.Equals(searchName, StringComparison.OrdinalIgnoreCase) || 
                    typeName.Contains(searchName))
                {
                    targetComp = comp;
                    break;
                }
            }

            if (targetComp == null)
            {
                var allComps = string.Join(", ", Array.ConvertAll(go.GetComponents<Component>(), c => c != null ? c.GetType().Name : "Missing"));
                return $"Error: Component '{searchName}' not found. Available components: {allComps}";
            }

            SerializedObject so = new SerializedObject(targetComp);
            SerializedProperty iterator = so.GetIterator();
            
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Inspector Fields for {targetComp.GetType().Name} on {arguments[0]}:");

            Type compType = targetComp.GetType();

            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                if (iterator.propertyPath == "m_Script") continue;

                string fieldTypeStr = GetFieldType(compType, iterator.propertyPath);
                string valueStr = GetPropertyValueAsString(iterator);

                sb.AppendLine($"- Name: {iterator.propertyPath}");
                sb.AppendLine($"  Type: {fieldTypeStr} ({iterator.propertyType})");
                sb.AppendLine($"  Value: {valueStr}");
                
                enterChildren = false; 
            }

            return sb.ToString();
        }

        private string GetFieldType(Type type, string propertyPath)
        {
            // Nested paths (e.g., "myStruct.myField") are simplified here.
            // For a more robust solution, recursive search would be needed.
            string topLevelName = propertyPath.Split('.')[0];
            FieldInfo field = type.GetField(topLevelName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return field.FieldType.Name;
            
            PropertyInfo prop = type.GetProperty(topLevelName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null) return prop.PropertyType.Name;

            return "Unknown";
        }

        private string GetPropertyValueAsString(SerializedProperty sp)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer: return sp.intValue.ToString();
                case SerializedPropertyType.Boolean: return sp.boolValue.ToString();
                case SerializedPropertyType.Float: return sp.floatValue.ToString();
                case SerializedPropertyType.String: return sp.stringValue;
                case SerializedPropertyType.Color: return sp.colorValue.ToString();
                case SerializedPropertyType.ObjectReference: return sp.objectReferenceValue != null ? $"{sp.objectReferenceValue.name} ({sp.objectReferenceValue.GetType().Name})" : "null";
                case SerializedPropertyType.Enum: return sp.enumDisplayNames.Length > sp.enumValueIndex && sp.enumValueIndex >= 0 ? sp.enumDisplayNames[sp.enumValueIndex] : sp.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2: return sp.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return sp.vector3Value.ToString();
                case SerializedPropertyType.Rect: return sp.rectValue.ToString();
                case SerializedPropertyType.ArraySize: return sp.intValue.ToString();
                case SerializedPropertyType.Character: return ((char)sp.intValue).ToString();
                case SerializedPropertyType.AnimationCurve: return "AnimationCurve";
                case SerializedPropertyType.Bounds: return sp.boundsValue.ToString();
                case SerializedPropertyType.Gradient: return "Gradient";
                case SerializedPropertyType.Quaternion: return sp.quaternionValue.ToString();
                case SerializedPropertyType.ExposedReference: return "ExposedReference";
                case SerializedPropertyType.FixedBufferSize: return sp.intValue.ToString();
                case SerializedPropertyType.Vector2Int: return sp.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int: return sp.vector3IntValue.ToString();
                case SerializedPropertyType.RectInt: return sp.rectIntValue.ToString();
                case SerializedPropertyType.BoundsInt: return sp.boundsIntValue.ToString();
                default: return "(Unsupported or Complex Type)";
            }
        }
    }
}