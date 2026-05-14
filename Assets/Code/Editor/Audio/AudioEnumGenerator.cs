using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace UsefulTools.Editor
{
    public static class AudioEnumGenerator
    {
        private const string SCRIPT_OUTPUT_PATH = "Assets/Code/AutoGenerate";
        private const string ASSET_OUTPUT_PATH = "Assets/Data/ScriptableObject";
        private const string SESSION_KEY_PENDING_UPDATE = "UsefulTools.Audio.PendingUpdate";

        [MenuItem("UsefulTools/Generate/Audio Enum")]
        public static void Generate()
        {
            bool bgmGenerated = GenerateEnumFiles(AudioSupportTool.BGMFolder, AudioSupportTool.BGMEnumName);
            bool seGenerated = GenerateEnumFiles(AudioSupportTool.SEFolder, AudioSupportTool.SEEnumName);

            if (bgmGenerated || seGenerated)
            {
                SessionState.SetBool(SESSION_KEY_PENDING_UPDATE, true);
                AssetDatabase.Refresh();
            }
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (SessionState.GetBool(SESSION_KEY_PENDING_UPDATE, false))
            {
                SessionState.SetBool(SESSION_KEY_PENDING_UPDATE, false);
                UpdateAllLibraryAssets();
            }
        }

        private static bool GenerateEnumFiles(string folder, string enumName)
        {
            if (!AudioSupportTool.GenerateEnumEnabled) return false;
            if (!AssetDatabase.IsValidFolder(folder)) return false;

            var namesAndClips = GetAudioClipsInFolder(folder);
            if (namesAndClips.Count == 0) return false;

            string[] names = namesAndClips.Keys.ToArray();
            EnumGenerator.GenerateEnum(enumName, names);

            string className = enumName + "Library";
            GenerateLibraryScript(className, enumName);

            return true;
        }

        public static void UpdateAllLibraryAssets()
        {
            UpdateLibraryAsset(AudioSupportTool.BGMFolder, AudioSupportTool.BGMEnumName);
            UpdateLibraryAsset(AudioSupportTool.SEFolder, AudioSupportTool.SEEnumName);
        }

        private static void UpdateLibraryAsset(string folder, string enumName)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;

            var namesAndClips = GetAudioClipsInFolder(folder);
            string className = enumName + "Library";
            string assetPath = Path.Combine(ASSET_OUTPUT_PATH, className + ".asset");

            if (!Directory.Exists(ASSET_OUTPUT_PATH)) Directory.CreateDirectory(ASSET_OUTPUT_PATH);

            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (asset == null)
            {
                string fullClassName = $"{CodeSupportTool.EnumNamespace}.{className}";
                Type type = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.FullName == fullClassName);

                if (type == null) return;

                asset = ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            SerializedObject so = new SerializedObject(asset);
            SerializedProperty clipsProp = so.FindProperty("Clips");
            clipsProp.ClearArray();

            int i = 0;
            foreach (var kvp in namesAndClips)
            {
                clipsProp.InsertArrayElementAtIndex(i);
                SerializedProperty element = clipsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Type").intValue = i; 
                element.FindPropertyRelative("Clip").objectReferenceValue = kvp.Value;
                i++;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static Dictionary<string, AudioClip> GetAudioClipsInFolder(string folderPath)
        {
            var results = new Dictionary<string, AudioClip>();
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folderPath });
            
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath)
                             .Where(p => p.StartsWith(folderPath))
                             .OrderBy(p => p)
                             .ToList();

            foreach (var path in paths)
            {
                string rawName = Path.GetFileNameWithoutExtension(path);
                
                // 日本語チェック
                if (HasJapanese(rawName))
                {
                    Debug.LogError($"[UsefulTools] Japanese characters detected in filename: {rawName}. Deleting asset.");
                    AssetDatabase.DeleteAsset(path);
                    continue;
                }

                string sanitizedName = SanitizeName(rawName);

                if (!results.ContainsKey(sanitizedName))
                {
                    results.Add(sanitizedName, AssetDatabase.LoadAssetAtPath<AudioClip>(path));
                }
                else
                {
                    Debug.LogWarning($"[UsefulTools] Duplicate sanitized name detected: {sanitizedName} (Path: {path}). Skipping.");
                }
            }
            return results;
        }

        private static void GenerateLibraryScript(string className, string enumName)
        {
            if (!Directory.Exists(SCRIPT_OUTPUT_PATH)) Directory.CreateDirectory(SCRIPT_OUTPUT_PATH);

            string ns = CodeSupportTool.EnumNamespace;
            string path = Path.Combine(SCRIPT_OUTPUT_PATH, className + ".cs");
            
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// 自動生成ファイルの為、手動での編集は上書きされます。");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [CreateAssetMenu(fileName = \"{className}\", menuName = \"UsefulTools/Audio/{className}\")]");
            sb.AppendLine($"    public class {className} : ScriptableObject");
            sb.AppendLine("    {");
            sb.AppendLine("        [Serializable]");
            sb.AppendLine("        public struct AudioPair");
            sb.AppendLine("        {");
            sb.AppendLine($"            public {enumName} Type;");
            sb.AppendLine("            public AudioClip Clip;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public List<AudioPair> Clips = new List<AudioPair>();");
            sb.AppendLine();
            sb.AppendLine($"        public AudioClip GetClip({enumName} type)");
            sb.AppendLine("        {");
            sb.AppendLine("            var pair = Clips.Find(p => p.Type == type);");
            sb.AppendLine("            return pair.Clip;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            string newContent = sb.ToString();
            if (File.Exists(path) && File.ReadAllText(path) == newContent) return;

            File.WriteAllText(path, newContent, Encoding.UTF8);
        }

        private static string SanitizeName(string name)
        {
            // 特殊記号をアンダースコアに変換（英数字以外）
            string sanitized = Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
            
            // 連続するアンダースコアをまとめる
            sanitized = Regex.Replace(sanitized, @"_+", "_");
            
            // 前後のアンダースコアを削除
            sanitized = sanitized.Trim('_');

            if (string.IsNullOrEmpty(sanitized)) return "None";
            if (char.IsDigit(sanitized[0])) return "_" + sanitized;
            
            return sanitized;
        }

        private static bool HasJapanese(string text)
        {
            // ひらがな、カタカナ、漢字の範囲をチェック
            return Regex.IsMatch(text, @"[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF]");
        }
    }
}
