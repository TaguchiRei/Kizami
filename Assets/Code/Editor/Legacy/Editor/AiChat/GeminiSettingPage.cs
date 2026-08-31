// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor.Ai
{
    public sealed class GeminiSettingPage
        : SettingPageBase
    {
        private GeminiSettings settings;

        public override string Name =>
            "Gemini";

        public override void Initialize()
        {
            settings =
                GeminiSettings.Load();
        }

        public override void OnGUI()
        {
            GUILayout.Space(5);

            EditorGUILayout.LabelField(
                "Gemini Settings",
                EditorStyles.boldLabel);
            
            
            settings.EnableAutoExecuteCommands =
                EditorGUILayout.Toggle(
                    "Enable Command Confirmation",
                    settings.EnableAutoExecuteCommands);

            GUILayout.Space(5);

            EditorGUILayout.LabelField("Personalized Instructions (System Prompt Suffix)");
            settings.SystemPromptSuffix = EditorGUILayout.TextArea(settings.SystemPromptSuffix, GUILayout.Height(60));

            GUILayout.Space(5);

            settings.ApiKey =
                EditorGUILayout.PasswordField(
                    "API Key",
                    settings.ApiKey);

            settings.ModelName =
                EditorGUILayout.TextField(
                    "Model Name (Internal)",
                    settings.ModelName);

            settings.SelectedModel = (GeminiModel)EditorGUILayout.EnumPopup(
                "Model",
                settings.SelectedModel);

            settings.Temperature =
                EditorGUILayout.Slider(
                    "Temperature",
                    settings.Temperature,
                    0.0f,
                    1.0f);

            settings.MaxOutputTokens =
                EditorGUILayout.IntField(
                    "Max Output Tokens",
                    settings.MaxOutputTokens);

            settings.TimeoutSeconds =
                EditorGUILayout.IntField(
                    "Timeout (Seconds)",
                    settings.TimeoutSeconds);

            GUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save"))
                {
                    settings.Save();

                    Debug.Log(
                        "Gemini settings saved.");
                }

                if (GUILayout.Button("Reload"))
                {
                    settings =
                        GeminiSettings.Load();
                }
            }

            GUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "Settings are stored in UserSettings/ and are not shared via Git.",
                MessageType.Info);
        }
    }
}
#endif
