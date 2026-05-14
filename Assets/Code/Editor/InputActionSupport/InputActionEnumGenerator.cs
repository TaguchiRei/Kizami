using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UsefulTools.Editor
{
    /// <summary>
    /// Project-wide Actionsに設定されたInputActionAssetから
    /// ActionMapとActionのenumを自動生成するエディタ拡張
    /// </summary>
    public class InputActionEnumGenerator
    {
        public static void GenerateAllEnums()
        {
            var asset = InputSystem.actions;

            if (asset == null)
            {
                Debug.LogError("[UsefulTools] Project-wide Actions が設定されていません。\nProject Settings > Input System Package > Project-wide Actions を設定してください。");
                return;
            }

            if (Generate(asset))
            {
                AssetDatabase.Refresh();
            }
        }

        public static bool Generate(InputActionAsset asset)
        {
            if (asset == null) return false;

            string outputFolder = InputSupportTool.OutputFolder;
            if (string.IsNullOrEmpty(outputFolder)) outputFolder = CodeSupportTool.EnumOutputFolder;
            
            string ns = CodeSupportTool.EnumNamespace;

            // ActionMap enum values
            var mapNames = asset.actionMaps.Select(m => SanitizeName(m.name)).ToArray();
            EnumGenerator.GenerateEnum("ActionMaps", mapNames, outputFolder, ns);

            // Action enum per ActionMap
            foreach (var map in asset.actionMaps)
            {
                string actionEnumName = $"{SanitizeName(map.name)}Actions";
                var actionNames = map.actions.Select(a => SanitizeName(a.name)).ToArray();
                EnumGenerator.GenerateEnum(actionEnumName, actionNames, outputFolder, ns);
            }

            Debug.Log($"[UsefulTools] Input enums generated for '{asset.name}' at: {outputFolder} with namespace {ns}");
            return true;
        }

        private static string SanitizeName(string name)
        {
            string sanitized = Regex.Replace(name, @"[^a-zA-Z0-9_]", "");

            if (string.IsNullOrEmpty(sanitized))
            {
                return "_";
            }

            if (char.IsDigit(sanitized[0]))
            {
                return "_" + sanitized;
            }

            return sanitized;
        }
    }
}