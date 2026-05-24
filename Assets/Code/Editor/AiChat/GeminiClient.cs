using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace UsefulTools.Editor.Ai
{
    public enum GeminiModel
    {
        Gemini2_5_Flash,
        Gemini2_5_Pro,
        Gemini3_1_Flash_Lite,
        Gemini3_1_Pro_Preview,
        Gemini3_5_Flash,
    }

    public static class GeminiModelExtensions
    {
        public static string ToModelId(
            this GeminiModel model)
        {
            return model switch
            {
                GeminiModel.Gemini2_5_Flash =>
                    "gemini-2.5-flash",

                GeminiModel.Gemini2_5_Pro =>
                    "gemini-2.5-pro",

                GeminiModel.Gemini3_1_Flash_Lite =>
                    "gemini-3.1-flash-lite",

                GeminiModel.Gemini3_1_Pro_Preview =>
                    "gemini-3.1-pro-preview",

                GeminiModel.Gemini3_5_Flash =>
                    "gemini-3.5-flash",

                _ =>
                    "gemini-2.5-flash"
            };
        }

        public static GeminiModel FromModelId(
            string modelId)
        {
            return modelId switch
            {
                "gemini-2.5-flash" =>
                    GeminiModel.Gemini2_5_Flash,

                "gemini-2.5-pro" =>
                    GeminiModel.Gemini2_5_Pro,

                "gemini-3.1-flash-lite" =>
                    GeminiModel.Gemini3_1_Flash_Lite,

                "gemini-3.1-pro-preview" =>
                    GeminiModel.Gemini3_1_Pro_Preview,

                "gemini-3.5-flash" =>
                    GeminiModel.Gemini3_5_Flash,

                _ =>
                    GeminiModel.Gemini2_5_Flash
            };
        }
    }

    public sealed class GeminiClient
    {
        private const int MaxRetryCount = 3;

        private readonly string apiKey;

        private readonly GeminiModel model;

        private readonly string modelName;

        private readonly List<Content>
            conversation =
                new();

        private string systemInstructionText =
            "";

        public GeminiClient(
            string apiKey,
            GeminiModel model =
                GeminiModel.Gemini2_5_Flash)
        {
            this.apiKey =
                apiKey?.Trim();

            this.model =
                model;

            modelName =
                model.ToModelId();

            systemInstructionText =
                "あなたはUnity/C#専門AIです。\n" +
                "\n" +
                "【重要：出力ルール】\n" +
                "返答は必ず以下のJSON形式に厳密に従ってください。\n" +
                "{\"message\": \"...\", \"intent\": \"...\", \"commands\": [{\"name\": \"コマンド名\", \"arguments\": [\"引数1\"]}]}\n" +
                "\n" +
                "【ルール】\n" +
                "1. 絶対に 'commands' 配列内には、以下の利用可能なコマンド一覧に記載されている正確な名前のみを使用してください。\n" +
                "2. 目的を達成するために必要なコマンドを配列に並べて一括出力してください。\n" +
                "3. **必要な情報はユーザーに質問する前に、まず利用可能なコマンドを実行して自律的に取得してください。**\n" +
                "4. **単に「使えるコマンドを教えて」等の質問に対しては、コマンドは実行せず、メッセージとして説明を返してください。**\n" +
                "\n" +
                "【利用可能なコマンド一覧】\n" +
                "AddComponent, SetComponentValue, GetComponentInspectorFields, GetComponentScriptPath, InvokeMenuItem, ChangeFile, PatchFile, Clear, ListFiles, ReadFile, DeleteFile, DeletePath, CaptureGameView, WriteFile, MoveFile, CopyFile, ReadDirectory, CreateDirectory, GetLoadedScenes, GetHierarchy, FindGameObjects, GetSelection, GetCompileErrors, Exists, FindAssets, CreateGameObject, AttachComponentReference, DeleteGameObject, EnterPlayMode, ExitPlayMode, IsPlaying, GetMaterialProperties, SetMaterialProperty, CreatePrefab, ApplyPrefabInstance\n" +
                "\n" +
                "例：\n" +
                "{\"message\": \"Assetsディレクトリとシーンを確認します\", \"intent\": \"Explore\", \"commands\": [{\"name\": \"ReadDirectory\", \"arguments\": [\"Assets\"]}, {\"name\": \"GetLoadedScenes\", \"arguments\": []}]}";
        }

        public void AddSystemInstruction(
            string text)
        {
            systemInstructionText +=
                "\n" + text;
        }

        public void ClearConversation()
        {
            conversation.Clear();
        }

        public void SetSystemInstruction(string text)
        {
            systemInstructionText = text;
        }

        public void AddToolResult(
            string commandName,
            string result)
        {
            StringBuilder builder =
                new();

            builder.AppendLine(
                "[ToolResult]");

            builder.AppendLine(
                $"Command: {commandName}");

            builder.AppendLine();

            builder.AppendLine(
                result);

            conversation.Add(
                Content.System(
                    builder.ToString()));
        }

        public string ExportConversationJson()
        {
            return JsonConvert.SerializeObject(
                conversation,
                Formatting.Indented);
        }

        public void ImportConversationJson(
            string json)
        {
            List<Content> imported =
                JsonConvert
                    .DeserializeObject
                    <List<Content>>(json);

            if (imported == null)
            {
                return;
            }

            conversation.Clear();

            conversation.AddRange(
                imported);
        }

        public List<(string role, string text)>
            GetConversationHistory()
        {
            return conversation
                .Select(c =>
                {
                    string text =
                        c.parts
                            .FirstOrDefault(
                                p =>
                                    !string.IsNullOrEmpty(
                                        p.text))
                            ?.text ?? "";

                    return (
                        c.role,
                        text);
                })
                .ToList();
        }

        public async Task<GeminiResponse>
            SendAsync(
                string message,
                string role = "user",
                string imagePath = null,
                List<FileContextItem> activeFiles = null,
                System.Threading.CancellationToken ct = default)
        {
            List<Part> parts =
                new();

            parts.Add(
                new Part
                {
                    text =
                        message
                });

            if (!string.IsNullOrEmpty(
                    imagePath) &&
                File.Exists(imagePath))
            {
                byte[] imageBytes =
                    File.ReadAllBytes(
                        imagePath);

                string base64Image =
                    Convert.ToBase64String(
                        imageBytes);

                parts.Add(
                    new Part
                    {
                        inline_data =
                            new InlineData
                            {
                                mime_type =
                                    "image/png",

                                data =
                                    base64Image
                            }
                    });
            }

            Content nextContent =
                new Content
                {
                    role = role,
                    parts = parts.ToArray()
                };

            string url =
                $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

            GenerateContentRequest
                requestBody =
                    BuildRequest(
                        nextContent,
                        activeFiles);

            Debug.Log($"[AI] Request Payload: {JsonConvert.SerializeObject(requestBody, Formatting.Indented)}");

            string json =
                JsonConvert.SerializeObject(
                    requestBody,
                    new JsonSerializerSettings
                    {
                        NullValueHandling =
                            NullValueHandling.Ignore
                    });

            for (int retry = 0;
                 retry < MaxRetryCount;
                 retry++)
            {
                using UnityWebRequest
                    request =
                        new UnityWebRequest(
                            url,
                            "POST");

                byte[] bodyRaw =
                    Encoding.UTF8.GetBytes(
                        json);

                request.uploadHandler =
                    new UploadHandlerRaw(
                        bodyRaw);

                request.downloadHandler =
                    new DownloadHandlerBuffer();

                request.SetRequestHeader(
                    "Content-Type",
                    "application/json");

                UnityWebRequestAsyncOperation
                    operation =
                        request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                bool success =
                    request.result ==
                    UnityWebRequest.Result.Success;

                if (success)
                {
                    conversation.Add(
                        nextContent);

                    return ParseResponse(
                        request.downloadHandler.text);
                }

                long code =
                    request.responseCode;

                bool retryable =
                    code == 429 ||
                    code == 500 ||
                    code == 503;

                if (!retryable)
                {
                    throw new Exception(
                        $"Gemini API Error ({code})\n" +
                        request.error +
                        "\n\n" +
                        request.downloadHandler.text);
                }

                if (retry >=
                    MaxRetryCount - 1)
                {
                    throw new Exception(
                        $"Gemini retry failed ({code})\n" +
                        request.downloadHandler.text);
                }

                int delayMs =
                    (retry + 1) * 2000;

                await Task.Delay(delayMs);
            }

            throw new Exception(
                "Gemini request failed.");
        }

        private GeminiResponse ParseResponse(
            string responseJson)
        {
            GenerateContentResponse
                apiResponse =
                    JsonConvert
                        .DeserializeObject
                        <GenerateContentResponse>(
                            responseJson);

            string rawText =
                apiResponse
                    ?.candidates?[0]
                    ?.content
                    ?.parts?[0]
                    ?.text;

            if (string.IsNullOrEmpty(
                    rawText))
            {
                throw new Exception(
                    "Gemini response empty.");
            }

            rawText =
                CleanupJsonText(rawText);

            try
            {
                StructuredResponse
                    structured =
                        JsonConvert
                            .DeserializeObject
                            <StructuredResponse>(
                                rawText);

                if (structured == null)
                {
                    throw new Exception(
                        "Structured response null.");
                }

                conversation.Add(
                    Content.Model(rawText));

                Debug.Log(
                    $"[AI] Message: {structured.message}");

                Debug.Log(
                    $"[AI] Intent: {structured.intent}");

                Debug.Log(
                    $"[AI] Commands: {structured.commands?.Count ?? 0}");

                foreach (var cmd in structured.commands)
                {
                    Debug.Log($"[AI] Debug: Command parsed - Name='{cmd.name}', Arguments='{string.Join(", ", cmd.arguments)}'");
                }

                var usage = apiResponse?.usageMetadata;
                return new GeminiResponse(
                    structured.message ?? "",
                    structured.intent ?? "none",
                    structured.commands ?? new List<GeminiCommand>(),
                    modelName,
                    usage?.promptTokenCount ?? 0,
                    usage?.candidatesTokenCount ?? 0,
                    usage?.totalTokenCount ?? 0);
            }
            catch (Exception e)
            {
                Debug.LogError("[AI] Failed parse structured response\n" + e);
                conversation.Add(Content.Model(rawText));
                return new GeminiResponse(rawText, "none", new List<GeminiCommand>(), modelName, 0, 0, 0);
            }
        }

        private static string CleanupJsonText(
            string text)
        {
            text =
                text.Trim();

            if (text.StartsWith(
                    "```json"))
            {
                text =
                    text.Substring(7);
            }

            if (text.StartsWith(
                    "```"))
            {
                text =
                    text.Substring(3);
            }

            if (text.EndsWith(
                    "```"))
            {
                text =
                    text.Substring(
                        0,
                        text.Length - 3);
            }

            return text.Trim();
        }

        private GenerateContentRequest
            BuildRequest(
                Content nextContent,
                List<FileContextItem> activeFiles = null)
        {
            List<Content> conversationCopy = new(conversation);
            GeminiSettings settings = GeminiSettings.Load();

            if (settings.EnableHistoryLimit && conversationCopy.Count > settings.MaxHistoryCount)
            {
                // 最新のメッセージをMaxHistoryCount分だけ残す
                conversationCopy = conversationCopy
                    .Skip(conversationCopy.Count - settings.MaxHistoryCount)
                    .ToList();
            }

            string fileInfo = "";
            if (activeFiles != null)
            {
                foreach (var file in activeFiles.Where(f => f.IsEnabled))
                {
                    fileInfo += $"\n\n--- File: {file.FileName} ---\n{file.Content}\n";
                }
            }

            List<Content> contentsForApi = new();
            foreach (var c in conversationCopy)
            {
                contentsForApi.Add(new Content { role = c.role == "system" ? "user" : c.role, parts = c.parts });
            }
            contentsForApi.Add(new Content { role = nextContent.role == "system" ? "user" : nextContent.role, parts = nextContent.parts });

            // コンテキスト情報の取得
            string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string selectedObj = Selection.activeGameObject != null ? Selection.activeGameObject.name : "なし";
            string selectedAsset = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : "なし";

            string contextInfo = $"\n\n【現在のエディタ状況】\n" +
                                 $"- 開いているシーン: {activeScene}\n" +
                                 $"- 選択中のGameObject: {selectedObj}\n" +
                                 $"- 選択中のファイル/フォルダ: {selectedAsset}";

            return new GenerateContentRequest
            {
                systemInstruction =
                    new SystemInstruction
                    {
                        parts =
                            new[]
                            {
                                new Part
                                {
                                    text = systemInstructionText + "\n" + settings.SystemPromptSuffix + fileInfo + contextInfo
                                }
                            }
                    },

                contents =
                    contentsForApi.ToArray(),

                generationConfig =
                    new GenerationConfig
                    {
                        temperature =
                            settings.Temperature,

                        maxOutputTokens =
                            settings.MaxOutputTokens,

                        responseMimeType =
                            "application/json",

                        responseSchema =
                            ResponseSchema.Create()
                    }
            };
        }

        public sealed class GeminiResponse
        {
            public string Message { get; }
            public string Intent { get; }
            public IReadOnlyList<GeminiCommand> Commands { get; }
            public string ModelName { get; }
            public int PromptTokens { get; }
            public int CandidatesTokens { get; }
            public int TotalTokens { get; }

            internal GeminiResponse(string message, string intent, List<GeminiCommand> commands, string modelName, int promptTokens, int candidatesTokens, int totalTokens)
            {
                Message = message;
                Intent = intent;
                Commands = commands;
                ModelName = modelName;
                PromptTokens = promptTokens;
                CandidatesTokens = candidatesTokens;
                TotalTokens = totalTokens;
            }
        }

        [Serializable]
        public sealed class GeminiCommand
        {
            public string name { get; set; }
            public List<string> arguments { get; set; }
        }

        [Serializable]
        private sealed class StructuredResponse
        {
            public string message;

            public string intent;

            public List<GeminiCommand>
                commands;
        }

        [Serializable]
        private sealed class GenerateContentRequest
        {
            public SystemInstruction
                systemInstruction;

            public Content[] contents;

            public GenerationConfig
                generationConfig;
        }

        [Serializable]
        private sealed class SystemInstruction
        {
            public Part[] parts;
        }

        [Serializable]
        private sealed class Content
        {
            public string role;

            public Part[] parts;

            public static Content System(
                string text)
            {
                return new Content
                {
                    role = "system",

                    parts =
                        new[]
                        {
                            new Part
                            {
                                text = text
                            }
                        }
                };
            }

            public static Content User(
                string text)
            {
                return new Content
                {
                    role = "user",

                    parts =
                        new[]
                        {
                            new Part
                            {
                                text = text
                            }
                        }
                };
            }

            public static Content Model(
                string text)
            {
                return new Content
                {
                    role = "model",

                    parts =
                        new[]
                        {
                            new Part
                            {
                                text = text
                            }
                        }
                };
            }
        }

        [Serializable]
        private sealed class Part
        {
            public string text;

            public InlineData inline_data;
        }

        [Serializable]
        private sealed class InlineData
        {
            public string mime_type;

            public string data;
        }

        [Serializable]
        private sealed class GenerationConfig
        {
            public float temperature;

            public int maxOutputTokens;

            public string responseMimeType;

            public object responseSchema;
        }

        private static class ResponseSchema
        {
            public static object Create()
            {
                return new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        message = new { type = "STRING" },
                        intent = new { type = "STRING" },
                        commands = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    name = new { type = "STRING" },
                                    arguments = new
                                    {
                                        type = "ARRAY",
                                        items = new { type = "STRING" }
                                    }
                                },
                                required = new[] { "name", "arguments" }
                            }
                        }
                    },
                    required = new[] { "message", "intent", "commands" }
                };
            }
        }

        [Serializable]
        private sealed class GenerateContentResponse
        {
            public Candidate[] candidates;
            public UsageMetadata usageMetadata;
        }

        [Serializable]
        private sealed class UsageMetadata
        {
            public int promptTokenCount;
            public int candidatesTokenCount;
            public int totalTokenCount;
        }

        [Serializable]
        private sealed class Candidate
        {
            public ResponseContent content;
        }

        [Serializable]
        private sealed class ResponseContent
        {
            public Part[] parts;
        }
    }
}