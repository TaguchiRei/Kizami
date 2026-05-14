using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor
{
    public class InputSupportTool : SettingPageBase
    {
        public override string Name => "Input Support";

        private const string InputTimingKey = "UsefulTools.Input.GenerateTiming";
        private const string InputOutputFolderKey = "UsefulTools.Input.OutputFolder";
        private const string IgnorePatternsKey = "UsefulTools.Input.IgnorePatterns";

        public static GenerateTiming Timing
        {
            get => (GenerateTiming)EditorPrefs.GetInt(InputTimingKey, (int)GenerateTiming.OnAssetChanged);
            private set => EditorPrefs.SetInt(InputTimingKey, (int)value);
        }

        public static string OutputFolder
        {
            get => EditorPrefs.GetString(InputOutputFolderKey, "Assets/Code/AutoGenerate");
            set => EditorPrefs.SetString(InputOutputFolderKey, value);
        }

        public static string IgnorePatterns
        {
            get => EditorPrefs.GetString(IgnorePatternsKey, "Default");
            set => EditorPrefs.SetString(IgnorePatternsKey, value);
        }

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("Input System Support Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Paths
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Path Settings", EditorStyles.miniBoldLabel);
                DrawPathField("Output Folder", OutputFolder, path => OutputFolder = path);
            }

            EditorGUILayout.Space();

            // Timing & Manual Generation
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Generation Timing", EditorStyles.miniBoldLabel);
                Timing = (GenerateTiming)EditorGUILayout.EnumPopup("Generate Timing", Timing);
                
                EditorGUILayout.Space(5);
                if (GUILayout.Button("Scan & Generate All Input Enums", GUILayout.Height(30)))
                {
                    InputActionEnumGenerator.GenerateAllEnums();
                }
            }

            EditorGUILayout.Space();

            // Code Structure & Filter
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Code Structure & Filters", EditorStyles.miniBoldLabel);
                IgnorePatterns = EditorGUILayout.TextField("Ignore Patterns", IgnorePatterns);
                EditorGUILayout.HelpBox("Comma separated (e.g., 'Default, Samples'). Assets containing these will be ignored.", MessageType.Info);
            }
        }

        private void DrawPathField(string label, string currentPath, System.Action<string> onPathChanged)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string newPath = EditorGUILayout.TextField(label, currentPath);
                if (newPath != currentPath) onPathChanged(newPath);

                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    string selectedPath = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
                    if (!string.IsNullOrEmpty(selectedPath))
                    {
                        if (selectedPath.StartsWith(Application.dataPath))
                        {
                            onPathChanged("Assets" + selectedPath.Substring(Application.dataPath.Length));
                        }
                    }
                }
            }
        }
    }
}
