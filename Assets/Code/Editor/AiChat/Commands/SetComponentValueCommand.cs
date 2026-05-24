using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace UsefulTools.Editor.Ai.Commands
{
    public class SetComponentValueCommand : IAiCommand
    {
        public string Name => "SetComponentValue";
        public string Description => "SetComponentValue [Path] [Component] [PropertyOrField] [Value]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 4)
                return "Error: Missing arguments. Expected 4 (Path, Component, PropertyOrField, Value).";

            var go = GameObjectResolver.Resolve(arguments[0]);
            if (go == null) return $"Error: GameObject not found at {arguments[0]}.";

            var comp = go.GetComponent(arguments[1]);
            if (comp == null) return $"Error: Component {arguments[1]} not found.";

            string memberName = arguments[2];
            string valueStr = arguments[3];

            // 1. SerializedObject経由で試行 (SerializeFieldやPublicフィールドに有効)
            SerializedObject so = new SerializedObject(comp);
            SerializedProperty sp = so.FindProperty(memberName);
            if (sp != null)
            {
                try
                {
                    if (SetSerializedPropertyValue(sp, valueStr))
                    {
                        so.ApplyModifiedProperties();
                        return $"Value {valueStr} set to SerializedProperty {memberName} successfully.";
                    }
                }
                catch (Exception e)
                {
                    return $"Error setting SerializedProperty {memberName}: {e.Message}";
                }
            }

            // 2. Reflection経由で試行 (SerializedObjectで取れない非公開フィールドやプロパティ)
            var type = comp.GetType();
            
            // フィールドの検索
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                try
                {
                    Undo.RecordObject(comp, $"Set field {memberName} on {go.name}");
                    var convertedValue = ParseValue(valueStr, field.FieldType);
                    field.SetValue(comp, convertedValue);
                    EditorUtility.SetDirty(comp);
                    return $"Value {valueStr} set to field {memberName} via Reflection successfully.";
                }
                catch (Exception e)
                {
                    return $"Error setting field {memberName} via Reflection: {e.Message}";
                }
            }

            // プロパティの検索
            var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                try
                {
                    Undo.RecordObject(comp, $"Set property {memberName} on {go.name}");
                    var convertedValue = ParseValue(valueStr, property.PropertyType);
                    property.SetValue(comp, convertedValue, null);
                    EditorUtility.SetDirty(comp);
                    return $"Value {valueStr} set to property {memberName} via Reflection successfully.";
                }
                catch (Exception e)
                {
                    return $"Error setting property {memberName} via Reflection: {e.Message}";
                }
            }

            return $"Error: Member {memberName} not found or not writable on component {arguments[1]}.";
        }

        private bool SetSerializedPropertyValue(SerializedProperty sp, string value)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer:
                    sp.intValue = int.Parse(value);
                    return true;
                case SerializedPropertyType.Boolean:
                    sp.boolValue = bool.Parse(value);
                    return true;
                case SerializedPropertyType.Float:
                    sp.floatValue = float.Parse(value);
                    return true;
                case SerializedPropertyType.String:
                    sp.stringValue = value;
                    return true;
                case SerializedPropertyType.Color:
                    if (ColorUtility.TryParseHtmlString(value, out Color color))
                    {
                        sp.colorValue = color;
                        return true;
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    sp.vector2Value = ParseVector2(value);
                    return true;
                case SerializedPropertyType.Vector3:
                    sp.vector3Value = ParseVector3(value);
                    return true;
                case SerializedPropertyType.Enum:
                    sp.enumValueIndex = FindEnumIndex(sp, value);
                    return true;
            }
            return false;
        }

        private object ParseValue(string value, Type targetType)
        {
            if (targetType == typeof(Vector2)) return ParseVector2(value);
            if (targetType == typeof(Vector3)) return ParseVector3(value);
            if (targetType == typeof(Color))
            {
                if (ColorUtility.TryParseHtmlString(value, out Color color)) return color;
            }
            if (targetType.IsEnum) return Enum.Parse(targetType, value, true);
            
            return Convert.ChangeType(value, targetType);
        }

        private Vector2 ParseVector2(string value)
        {
            var parts = value.Trim('(', ')').Split(',');
            return new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
        }

        private Vector3 ParseVector3(string value)
        {
            var parts = value.Trim('(', ')').Split(',');
            return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
        }

        private int FindEnumIndex(SerializedProperty sp, string value)
        {
            if (int.TryParse(value, out int result)) return result;
            string[] names = sp.enumNames;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i].Equals(value, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }
    }
}