using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor
{
    public class AudioSupportTool : SettingPageBase
    {
        public override string Name => "Audio Support";

        private const string AutoRenameEnabledKey = "UsefulTools.Audio.AutoRenameEnabled";
        private const string GenerateEnumEnabledKey = "UsefulTools.Audio.GenerateEnumEnabled";
        private const string AutoCategoryEnabledKey = "UsefulTools.Audio.AutoCategoryEnabled";
        private const string AutoCategoryThresholdKey = "UsefulTools.Audio.AutoCategoryThreshold";
        private const string BGMEnumNameKey = "UsefulTools.Audio.BGMEnumName";
        private const string SEEnumNameKey = "UsefulTools.Audio.SEEnumName";
        private const string BGMFolderKey = "UsefulTools.Audio.BGMFolder";
        private const string SEFolderKey = "UsefulTools.Audio.SEFolder";

        public static bool AutoRenameEnabled { get => EditorPrefs.GetBool(AutoRenameEnabledKey, true); set => EditorPrefs.SetBool(AutoRenameEnabledKey, value); }
        public static bool GenerateEnumEnabled { get => EditorPrefs.GetBool(GenerateEnumEnabledKey, true); set => EditorPrefs.SetBool(GenerateEnumEnabledKey, value); }
        public static bool AutoCategoryEnabled { get => EditorPrefs.GetBool(AutoCategoryEnabledKey, false); set => EditorPrefs.SetBool(AutoCategoryEnabledKey, value); }
        public static float AutoCategoryThreshold { get => EditorPrefs.GetFloat(AutoCategoryThresholdKey, 30f); set => EditorPrefs.SetFloat(AutoCategoryThresholdKey, value); }
        
        public static string BGMEnumName { get => EditorPrefs.GetString(BGMEnumNameKey, "BGMType"); set => EditorPrefs.SetString(BGMEnumNameKey, value); }
        public static string SEEnumName { get => EditorPrefs.GetString(SEEnumNameKey, "SEType"); set => EditorPrefs.SetString(SEEnumNameKey, value); }

        public static string BGMFolder { get => EditorPrefs.GetString(BGMFolderKey, "Assets/Audio/BGM"); set => EditorPrefs.SetString(BGMFolderKey, value); }
        public static string SEFolder { get => EditorPrefs.GetString(SEFolderKey, "Assets/Audio/SE"); set => EditorPrefs.SetString(SEFolderKey, value); }

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("Audio Support Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Import Settings", EditorStyles.miniBoldLabel);
                AutoRenameEnabled = EditorGUILayout.Toggle("Enable Auto Rename", AutoRenameEnabled);
                GenerateEnumEnabled = EditorGUILayout.Toggle("Enable Enum Generation", GenerateEnumEnabled);
                
                EditorGUILayout.Space(5);
                
                AutoCategoryEnabled = EditorGUILayout.Toggle("Enable Auto Category", AutoCategoryEnabled);
                using (new EditorGUI.DisabledGroupScope(!AutoCategoryEnabled))
                {
                    AutoCategoryThreshold = EditorGUILayout.FloatField("BGM Threshold (sec)", AutoCategoryThreshold);
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Path & Enum Settings", EditorStyles.miniBoldLabel);
                
                BGMFolder = EditorGUILayout.TextField("BGM Folder", BGMFolder);
                BGMEnumName = EditorGUILayout.TextField("BGM Enum Name", BGMEnumName);
                
                EditorGUILayout.Space(5);
                
                SEFolder = EditorGUILayout.TextField("SE Folder", SEFolder);
                SEEnumName = EditorGUILayout.TextField("SE Enum Name", SEEnumName);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Manual Generate Audio Enums", GUILayout.Height(30)))
            {
                AudioEnumGenerator.Generate();
            }
        }
    }
}
