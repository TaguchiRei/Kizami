using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace UsefulTools.Editor
{
    public class CodeSupportTool : SettingPageBase
    {
        public override string Name => "Code Support";

        // --- Code Generation Keys ---
        private const string DefaultAccessModifierKey = "UsefulTools.Code.DefaultAccessModifier";
        private const string DefaultUseSummaryKey = "UsefulTools.Code.DefaultUseSummary";
        private const string DefaultIsSerializableKey = "UsefulTools.Code.DefaultIsSerializable";
        private const string DefaultNamespaceKey = "UsefulTools.Code.DefaultNamespace";

        // --- Enum Generation Keys ---
        private const string EnumOutputFolderKey = "UsefulTools.Code.EnumOutputFolder";
        private const string EnumNamespaceKey = "UsefulTools.Code.EnumNamespace";

        private const string DefaultEnumOutputFolder = "Assets/Code/AutoGenerate";
        private const string DefaultEnumNamespace = "UsefulTools.AutoGenerate";

        // --- URL Keys ---
        private const string UrlCSharpRefKey = "UsefulTools.Code.UrlCSharp";
        private const string UrlUnityApiKey = "UsefulTools.Code.UrlUnityApi";
        private const string UrlCustomKey = "UsefulTools.Code.UrlCustom";

        private const string DefaultUrlCSharp = "https://learn.microsoft.com/en-us/dotnet/csharp/";
        private const string DefaultUrlUnityApi = "https://docs.unity3d.com/ScriptReference/";

        public enum AccessModifier { Public, Protected, Internal, Private }

        public static AccessModifier DefaultAccess { get => (AccessModifier)EditorPrefs.GetInt(DefaultAccessModifierKey, (int)AccessModifier.Public); set => EditorPrefs.SetInt(DefaultAccessModifierKey, (int)value); }
        public static bool DefaultUseSummary { get => EditorPrefs.GetBool(DefaultUseSummaryKey, true); set => EditorPrefs.SetBool(DefaultUseSummaryKey, value); }
        public static bool DefaultIsSerializable { get => EditorPrefs.GetBool(DefaultIsSerializableKey, false); set => EditorPrefs.SetBool(DefaultIsSerializableKey, value); }
        public static string DefaultNamespace { get => EditorPrefs.GetString(DefaultNamespaceKey, "MyGame"); set => EditorPrefs.SetString(DefaultNamespaceKey, value); }

        public static string EnumOutputFolder { get => EditorPrefs.GetString(EnumOutputFolderKey, DefaultEnumOutputFolder); set => EditorPrefs.SetString(EnumOutputFolderKey, value); }
        public static string EnumNamespace { get => EditorPrefs.GetString(EnumNamespaceKey, DefaultEnumNamespace); set => EditorPrefs.SetString(EnumNamespaceKey, value); }

        public static string UrlCSharp { get => EditorPrefs.GetString(UrlCSharpRefKey, DefaultUrlCSharp); set => EditorPrefs.SetString(UrlCSharpRefKey, value); }
        public static string UrlUnityApi { get => EditorPrefs.GetString(UrlUnityApiKey, DefaultUrlUnityApi); set => EditorPrefs.SetString(UrlUnityApiKey, value); }
        public static string UrlCustom { get => EditorPrefs.GetString(UrlCustomKey, ""); set => EditorPrefs.SetString(UrlCustomKey, value); }

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("Code Support Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 1. Generation Defaults
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Code Generation Defaults", EditorStyles.miniBoldLabel);
                DefaultNamespace = EditorGUILayout.TextField("Default Namespace", DefaultNamespace);
                DefaultAccess = (AccessModifier)EditorGUILayout.EnumPopup("Access Modifier", DefaultAccess);
                DefaultUseSummary = EditorGUILayout.Toggle("Default Summary", DefaultUseSummary);
                DefaultIsSerializable = EditorGUILayout.Toggle("Default Serializable", DefaultIsSerializable);
                
                if (GUILayout.Button("Open Code Generator", GUILayout.Height(25))) CodeGenerator.ShowWindow();
            }

            EditorGUILayout.Space();

            // 2. Enum Generation Settings
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Enum Generation Defaults", EditorStyles.miniBoldLabel);
                EnumOutputFolder = EditorGUILayout.TextField("Output Folder", EnumOutputFolder);
                EnumNamespace = EditorGUILayout.TextField("Enum Namespace", EnumNamespace);

                if (GUILayout.Button("Reset to Default Settings"))
                {
                    EnumOutputFolder = DefaultEnumOutputFolder;
                    EnumNamespace = DefaultEnumNamespace;
                }
            }

            EditorGUILayout.Space();

            // 2. Reference URLs
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Reference URLs", EditorStyles.miniBoldLabel);
                
                DrawUrlField("C# Reference", UrlCSharp, url => UrlCSharp = url);
                DrawUrlField("Unity API", UrlUnityApi, url => UrlUnityApi = url);
                DrawUrlField("Custom URL", UrlCustom, url => UrlCustom = url);

                EditorGUILayout.Space(5);
                if (GUILayout.Button("Reset to Default URLs"))
                {
                    UrlCSharp = DefaultUrlCSharp;
                    UrlUnityApi = DefaultUrlUnityApi;
                    UrlCustom = "";
                }
            }
        }

        private void DrawUrlField(string label, string currentUrl, System.Action<string> onUrlChanged)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string newUrl = EditorGUILayout.TextField(label, currentUrl);
                if (newUrl != currentUrl) onUrlChanged(newUrl);

                if (GUILayout.Button("Open", GUILayout.Width(50)))
                {
                    if (!string.IsNullOrEmpty(currentUrl)) Application.OpenURL(currentUrl);
                }
            }
        }
    }
}
