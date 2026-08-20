// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;

namespace UsefulTools.Editor.Ai
{
    [Serializable]
    public sealed class GeminiSettings
    {
        public string ApiKey = "";
        public string ModelName = "gemini-1.5-flash"; 
        
        public GeminiModel SelectedModel = GeminiModel.Gemini2_5_Flash;
        
        public float Temperature = 0.7f;

        public int MaxOutputTokens = 8192;
        public bool EnableAutoExecuteCommands = false;
        public string SystemPromptSuffix = "";

        public bool EnableHistoryLimit = false;
        public int MaxHistoryCount = 10;

        public int TimeoutSeconds = 30;

        public GeminiModel GetModelEnum() => GeminiModelExtensions.FromModelId(ModelName);

        private const string SavePath =
            "UserSettings/UsefulTools.GeminiSettings.json";

        public void Save()
        {
            ModelName = SelectedModel.ToModelId();
            var json =
                UnityEngine.JsonUtility.ToJson(this, true);

            System.IO.File.WriteAllText(
                SavePath,
                json);
        }

        public static GeminiSettings Load()
        {
            if (!System.IO.File.Exists(SavePath))
            {
                return new GeminiSettings();
            }

            var json =
                System.IO.File.ReadAllText(SavePath);

            var settings = UnityEngine.JsonUtility
                .FromJson<GeminiSettings>(json);
            
            settings.SelectedModel = GeminiModelExtensions.FromModelId(settings.ModelName);
            return settings;
        }
    }
}
#endif
