using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor
{
    public class SceneSupportTool : SettingPageBase
    {
        public override string Name => "Scene Support";

        // Timing & Filter Keys
        private const string SceneEnumGeneratorTimingKey = "SceneEnumGenerator.GenerateTiming";
        private const string SceneEnumNamespaceKey = "UsefulTools.Path.EnumNamespace";
        private const string InListEnumNameKey = "UsefulTools.Path.InListEnumName";
        private const string OutListEnumNameKey = "UsefulTools.Path.OutListEnumName";
        private const string IgnorePatternsKey = "UsefulTools.Path.IgnorePatterns";

        // Path Keys
        private const string SceneSearchPathKey = "UsefulTools.Path.SceneSearch";
        private const string EnumOutputPathKey = "UsefulTools.Path.EnumOutput";

        public static GenerateTiming Timing
        {
            get => (GenerateTiming)EditorPrefs.GetInt(SceneEnumGeneratorTimingKey, (int)GenerateTiming.OnAssetChanged);
            private set => EditorPrefs.SetInt(SceneEnumGeneratorTimingKey, (int)value);
        }

        public static string Namespace
        {
            get => EditorPrefs.GetString(SceneEnumNamespaceKey, "UsefulTools.AutoGenerate");
            set => EditorPrefs.SetString(SceneEnumNamespaceKey, value);
        }

        public static string InListEnumName
        {
            get => EditorPrefs.GetString(InListEnumNameKey, "InListSceneName");
            set => EditorPrefs.SetString(InListEnumNameKey, value);
        }

        public static string OutListEnumName
        {
            get => EditorPrefs.GetString(OutListEnumNameKey, "OutListSceneName");
            set => EditorPrefs.SetString(OutListEnumNameKey, value);
        }

        public static string IgnorePatterns
        {
            get => EditorPrefs.GetString(IgnorePatternsKey, "");
            set => EditorPrefs.SetString(IgnorePatternsKey, value);
        }

        public static string SceneSearchPath
        {
            get => EditorPrefs.GetString(SceneSearchPathKey, "Assets/Level/Scenes");
            set => EditorPrefs.SetString(SceneSearchPathKey, value);
        }

        public static string EnumOutputPath
        {
            get => EditorPrefs.GetString(EnumOutputPathKey, "Assets/Code/AutoGenerate/SceneEnum.cs");
            set => EditorPrefs.SetString(EnumOutputPathKey, value);
        }

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("Scene Support Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Paths
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Path Settings", EditorStyles.miniBoldLabel);
                DrawPathField("Scene Search Root", SceneSearchPath, path => SceneSearchPath = path, true);
                DrawPathField("Enum Output File", EnumOutputPath, path => EnumOutputPath = path, false);
            }

            EditorGUILayout.Space();

            // Timing & Generation Button
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Generation Timing", EditorStyles.miniBoldLabel);
                Timing = (GenerateTiming)EditorGUILayout.EnumPopup("Generate Timing", Timing);
                
                EditorGUILayout.Space(5);
                if (GUILayout.Button("Generate Scene Enum Now", GUILayout.Height(30)))
                {
                    SceneEnumGenerator.Generate();
                }
            }

            EditorGUILayout.Space();

            // Code Structure
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Code Structure Settings", EditorStyles.miniBoldLabel);
                Namespace = EditorGUILayout.TextField("Namespace", Namespace);
                InListEnumName = EditorGUILayout.TextField("In-List Enum Name", InListEnumName);
                OutListEnumName = EditorGUILayout.TextField("Out-List Enum Name", OutListEnumName);
            }

            EditorGUILayout.Space();

            // Filters
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Filter Settings", EditorStyles.miniBoldLabel);
                IgnorePatterns = EditorGUILayout.TextField("Ignore Patterns", IgnorePatterns);
                EditorGUILayout.HelpBox("Comma separated (e.g., 'Test, _Temp').", MessageType.Info);
            }
        }

        private void DrawPathField(string label, string currentPath, System.Action<string> onPathChanged, bool isFolder)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string newPath = EditorGUILayout.TextField(label, currentPath);
                if (newPath != currentPath) onPathChanged(newPath);

                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    string selectedPath = isFolder 
                        ? EditorUtility.OpenFolderPanel("Select Folder", "Assets", "")
                        : EditorUtility.SaveFilePanel("Select File Location", "Assets", "SceneEnum", "cs");

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
