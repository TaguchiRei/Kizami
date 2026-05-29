using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

namespace UsefulTools.Editor.Ai
{
    [Serializable]
    public sealed class AiChatContext
    {
        [Serializable]
        public struct ChatMessageData
        {
            public string sender;
            public string message;
            public bool isUser;
            public int promptTokens;
            public int totalTokens;
        }

        public List<ChatMessageData> History = new List<ChatMessageData>();
        public List<FileContextItem> FileContexts = new List<FileContextItem>();
        public string GeminiConversationJson;

        private const string SavePath = "UserSettings/UsefulTools.AiChatContext.json";

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SavePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI] Failed to save chat context: {e.Message}");
            }
        }

        public static AiChatContext Load()
        {
            if (!File.Exists(SavePath))
            {
                return new AiChatContext();
            }

            try
            {
                var json = File.ReadAllText(SavePath);
                return FromJson(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AI] Failed to load chat context: {e.Message}");
                return new AiChatContext();
            }
        }

        public static AiChatContext FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new AiChatContext();

            string trimmed = json.Trim();
            
            // 1. 純粋な配列形式の場合
            if (trimmed.StartsWith("["))
            {
                return new AiChatContext
                {
                    GeminiConversationJson = json,
                    History = new List<ChatMessageData>(),
                    FileContexts = new List<FileContextItem>()
                };
            }

            // 2. オブジェクト形式の場合
            try
            {
                // 一旦 JObject としてパースして中身を確認
                var jobj = Newtonsoft.Json.Linq.JObject.Parse(json);
                
                // "contents" キーがある場合は、その配列部分を抽出して扱う
                if (jobj.TryGetValue("contents", out var contentsToken) && contentsToken.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                {
                    return new AiChatContext
                    {
                        GeminiConversationJson = contentsToken.ToString(),
                        History = new List<ChatMessageData>(),
                        FileContexts = new List<FileContextItem>()
                    };
                }

                // それ以外は通常の AiChatContext としてデシリアライズ
                return JsonConvert.DeserializeObject<AiChatContext>(json) ?? new AiChatContext();
            }
            catch
            {
                // パース失敗時の最終フォールバック
                return new AiChatContext();
            }
        }
        
        public static void Clear()
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }
        }
    }
}
