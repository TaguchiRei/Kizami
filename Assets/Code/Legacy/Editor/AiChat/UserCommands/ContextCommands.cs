// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor.Ai.UserCommands
{
    public class SaveContextUserCommand : IUserCommand
    {
        public string Name => "SaveContext";
        public string Description => "会話履歴・状態を保存します";
        public void Execute(string[] args)
        {
            var window = EditorWindow.GetWindow<AiChatWindow>();
            if (window == null) return;

            string path = EditorUtility.SaveFilePanel("Save Conversation Context", "", "ConversationContext.json", "json");
            if (!string.IsNullOrEmpty(path))
            {
                var context = window.ExportContext();
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(context, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(path, json);
                Debug.Log($"User Command: SaveContext executed. Saved to {path}");
            }
        }
    }

    public class LoadContextUserCommand : IUserCommand
    {
        public string Name => "LoadContext";
        public string Description => "保存済みContextを読み込みます";
        public void Execute(string[] args)
        {
            var window = EditorWindow.GetWindow<AiChatWindow>();
            if (window == null) return;

            string path = EditorUtility.OpenFilePanel("Load Conversation Context", "", "json");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(path);
                    var context = AiChatContext.FromJson(json);
                    window.ImportContext(context);
                    Debug.Log($"User Command: LoadContext executed. Loaded from {path}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to load context: {e.Message}");
                }
            }
        }
    }

    public class SummaryUserCommand : IUserCommand
    {
        public string Name => "Summary";
        public string Description => "会話を要約し、要約を元に会話をリセットします";

        public async void Execute(string[] args)
        {
            var window = EditorWindow.GetWindow<AiChatWindow>();
            if (window == null) return;

            string fullHistory = window.GetFullHistoryText();
            if (string.IsNullOrEmpty(fullHistory))
            {
                Debug.Log("No history to summarize.");
                return;
            }

            string summary = await window.RequestSummary(fullHistory);
            
            window.ClearConversation();
            window.AddMessage("System", $"会話を要約しました:\n{summary}", false, 0, 0);
            window.SetInitialContext($"以下の要約をベースに会話を継続してください。\n\n[要約]\n{summary}");
            
            Debug.Log($"User Command: Summary executed. New context set: {summary}");
        }
    }
}
#endif
