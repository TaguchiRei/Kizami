// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class GetMaterialPropertiesCommand : IAiCommand
    {
        public string Name => "GetMaterialProperties";
        public string Description => "GetMaterialProperties [AssetPath]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 1) return "Error: Missing path.";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(arguments[0]);
            if (mat == null) return $"Error: Material not found at {arguments[0]}.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Material Properties for {arguments[0]}:");
            sb.AppendLine($"Shader: {mat.shader.name}");

            var shader = mat.shader;
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                string propertyName = ShaderUtil.GetPropertyName(shader, i);
                var type = ShaderUtil.GetPropertyType(shader, i);
                string value = "";

                switch (type)
                {
                    case ShaderUtil.ShaderPropertyType.Color: value = mat.GetColor(propertyName).ToString(); break;
                    case ShaderUtil.ShaderPropertyType.Vector: value = mat.GetVector(propertyName).ToString(); break;
                    case ShaderUtil.ShaderPropertyType.Float: 
                    case ShaderUtil.ShaderPropertyType.Range: value = mat.GetFloat(propertyName).ToString(); break;
                    case ShaderUtil.ShaderPropertyType.TexEnv: 
                        var tex = mat.GetTexture(propertyName);
                        value = tex != null ? $"{tex.name} ({AssetDatabase.GetAssetPath(tex)})" : "null"; 
                        break;
                }
                sb.AppendLine($"- {propertyName} ({type}): {value}");
            }

            return sb.ToString();
        }
    }

    public class SetMaterialPropertyCommand : IAiCommand
    {
        public string Name => "SetMaterialProperty";
        public string Description => "SetMaterialProperty [AssetPath] [PropName] [Value]";

        public string Execute(List<string> arguments)
        {
            if (arguments.Count < 3) return "Error: Missing arguments. Expected 3 (Path, PropName, Value).";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(arguments[0]);
            if (mat == null) return $"Error: Material not found at {arguments[0]}.";

            string propName = arguments[1];
            string value = arguments[2];

            Undo.RecordObject(mat, $"Set Material Property {propName}");

            try
            {
                if (ColorUtility.TryParseHtmlString(value, out Color color))
                {
                    mat.SetColor(propName, color);
                }
                else if (float.TryParse(value, out float f))
                {
                    mat.SetFloat(propName, f);
                }
                else if (value.StartsWith("(") && value.EndsWith(")")) // Vectorパース
                {
                    var parts = value.Trim('(', ')').Split(',');
                    if (parts.Length == 4) mat.SetVector(propName, new Vector4(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3])));
                    else if (parts.Length == 3) mat.SetVector(propName, new Vector4(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]), 0));
                }
                else
                {
                    // テクスチャ設定を試みる（パス指定と仮定）
                    var tex = AssetDatabase.LoadAssetAtPath<Texture>(value);
                    if (tex != null) mat.SetTexture(propName, tex);
                    else return $"Error: Invalid value format or texture not found for {propName}.";
                }

                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
                return $"Property {propName} set successfully on {arguments[0]}.";
            }
            catch (Exception e)
            {
                return $"Error setting property: {e.Message}";
            }
        }
    }
}
#endif
