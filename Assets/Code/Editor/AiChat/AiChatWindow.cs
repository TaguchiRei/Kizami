using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Threading;

namespace UsefulTools.Editor.Ai
{
    public class AiChatWindow : EditorWindow
    {
        [MenuItem("UsefulTools/AI Chat")]
        public static void ShowWindow()
        {
            AiChatWindow wnd = GetWindow<AiChatWindow>();
            wnd.titleContent = new GUIContent("AI Chat");
            wnd.minSize = new Vector2(350, 500);
        }

        private ScrollView _chatHistory;
        private TextField _inputField;
        private Button _sendButton;
        private Button _stopButton;
        private GeminiClient _client;
        private GeminiSettings _settings;
        private VisualElement _commandPanel;
        private Label _statusLabel;
        private Label _tokenLabel;
        private CancellationTokenSource _cts;

        private List<FileContextItem> _fileContexts = new List<FileContextItem>();
        private VisualElement _fileContextPanel;
        private VisualElement _rightPanel;

        private struct ChatMessage { public string sender; public string message; public bool isUser; public int promptTokens; public int totalTokens; }
        private List<ChatMessage> _persistentHistory = new List<ChatMessage>();
        
        
        public void CreateGUI()
        {
            // 既存UIの二重生成対策
            rootVisualElement.Clear();

            // 状態依存は保証しない（OnEnable側で担保）
            if (_settings == null)
                _settings = GeminiSettings.Load();

            if (_client == null && _settings != null)
                _client = new GeminiClient(_settings.ApiKey, _settings.GetModelEnum());

            RegisterCommands();

            var root = rootVisualElement;
            ApplyRootStyle(root);

            BuildLayout(root);
            RestoreHistoryUI();
        }
        
        private void BuildLayout(VisualElement root)
        {
            var mainContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexGrow = 1
                }
            };
            root.Add(mainContainer);

            // 左：チャット領域
            var chatArea = new VisualElement();
            chatArea.style.flexGrow = 1;
            mainContainer.Add(chatArea);

            BuildHeader(chatArea);
            BuildStatus(chatArea);
            BuildChatHistory(chatArea);
            BuildInputArea(chatArea);

            // 右パネル
            _rightPanel = new VisualElement();
            ApplyRightPanelStyle(_rightPanel);
            mainContainer.Add(_rightPanel);

            InitializeRightPanel();
        }
        private void BuildHeader(VisualElement parent)
        {
            var header = new VisualElement();
            ApplyHeaderStyle(header);

            var title = new Label("Gemini AI Chat");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            header.Add(title);

            var clearButton = new Button(OnClearClicked) { text = "Clear" };
            ApplyHeaderButtonStyle(clearButton);
            header.Add(clearButton);

            var toggleCommandButton = new Button(OnToggleCommandPanelClicked)
            {
                text = "Commands >>"
            };
            ApplyHeaderButtonStyle(toggleCommandButton);
            header.Add(toggleCommandButton);

            parent.Add(header);
        }
        
        private void BuildStatus(VisualElement parent)
        {
            _statusLabel = new Label("");
            _statusLabel.style.paddingLeft = 15;
            _statusLabel.style.fontSize = 11;
            parent.Add(_statusLabel);

            _tokenLabel = new Label("Prompt: 0 | Total: 0");
            _tokenLabel.style.paddingRight = 15;
            _tokenLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _tokenLabel.style.fontSize = 10;
            _tokenLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            parent.Add(_tokenLabel);
        }
        
        private void BuildChatHistory(VisualElement parent)
        {
            _chatHistory = new ScrollView(ScrollViewMode.Vertical);
            _chatHistory.style.flexGrow = 1;
            _chatHistory.style.paddingBottom = 20;
            _chatHistory.style.paddingTop = 20;
            _chatHistory.style.paddingLeft = 15;
            _chatHistory.style.paddingRight = 15;

            parent.Add(_chatHistory);
        }
        
        private void BuildInputArea(VisualElement parent)
        {
            var inputArea = new VisualElement();
            ApplyInputAreaStyle(inputArea);

            _inputField = new TextField();
            _inputField.style.flexGrow = 1;
            _inputField.style.flexShrink = 1;
            _inputField.style.flexBasis = 0; // 内容によって親を押し広げないように
            _inputField.multiline = true;
            _inputField.RegisterCallback<KeyDownEvent>(OnKeyDown);
            ApplyTextFieldStyle(_inputField);
            inputArea.Add(_inputField);

            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.flexShrink = 0; // ボタンが潰されたり押し出されたりしないように固定

            _sendButton = new Button(OnSendClicked) { text = "Send" };
            ApplySendButtonStyle(_sendButton);
            buttonContainer.Add(_sendButton);

            _stopButton = new Button(OnStopClicked) { text = "Stop" };
            _stopButton.SetEnabled(false);
            ApplyStopButtonStyle(_stopButton);
            buttonContainer.Add(_stopButton);

            inputArea.Add(buttonContainer);
            parent.Add(inputArea);
        }
        
        private void RestoreHistoryUI()
        {
            if (_chatHistory == null)
                return;

            _chatHistory.Clear();

            if (_persistentHistory == null || _persistentHistory.Count == 0)
            {
                AddMessage("Gemini",
                    "Unity/C#に関する質問があれば何でも聞いてください！\n\"/\"でコマンドを利用できます。",
                    false, 0, 0);
                return;
            }

            foreach (var msg in _persistentHistory)
            {
                AddMessageInternal(
                    msg.sender,
                    msg.message,
                    msg.isUser,
                    msg.promptTokens,
                    msg.totalTokens
                );
            }
        }
        
        private void OnEnable()
        {
            _settings = GeminiSettings.Load();

            if (_settings != null)
            {
                _client = new GeminiClient(
                    _settings.ApiKey,
                    _settings.GetModelEnum()
                );
            }

            _fileContexts ??= new List<FileContextItem>();
            _persistentHistory ??= new List<ChatMessage>();
        }
        private void RegisterCommands()
        {
            AiCommandRegistry.Register(new Commands.AddComponentCommand());
            AiCommandRegistry.Register(new Commands.SetComponentValueCommand());
            AiCommandRegistry.Register(new Commands.GetComponentInspectorFieldsCommand());
            AiCommandRegistry.Register(new Commands.GetComponentScriptPathCommand());
            AiCommandRegistry.Register(new Commands.InvokeMenuItemCommand());
            AiCommandRegistry.Register(new Commands.ChangeFileCommand());
            AiCommandRegistry.Register(new Commands.ClearContextCommand());
            AiCommandRegistry.Register(new Commands.ListFilesCommand());
            AiCommandRegistry.Register(new Commands.ReadFileCommand());
            AiCommandRegistry.Register(new Commands.DeletePathCommand());
            AiCommandRegistry.Register(new Commands.DeleteFileCommand());
            AiCommandRegistry.Register(new Commands.CaptureGameViewCommand());
            AiCommandRegistry.Register(new Commands.WriteFileCommand());
            AiCommandRegistry.Register(new Commands.MoveFileCommand());
            AiCommandRegistry.Register(new Commands.CopyFileCommand());
            AiCommandRegistry.Register(new Commands.ReadDirectoryCommand());
            AiCommandRegistry.Register(new Commands.CreateDirectoryCommand());
            AiCommandRegistry.Register(new Commands.GetLoadedScenesCommand());
            AiCommandRegistry.Register(new Commands.GetHierarchyCommand());
            AiCommandRegistry.Register(new Commands.FindGameObjectsCommand());
            AiCommandRegistry.Register(new Commands.GetSelectionCommand());
            AiCommandRegistry.Register(new Commands.GetCompileErrorsCommand());
            AiCommandRegistry.Register(new Commands.ExistsCommand());
            AiCommandRegistry.Register(new Commands.FindAssetsCommand());
            AiCommandRegistry.Register(new Commands.CreateGameObjectCommand());
            AiCommandRegistry.Register(new Commands.AttachComponentReferenceCommand());
            AiCommandRegistry.Register(new Commands.PatchFileCommand());
            AiCommandRegistry.Register(new Commands.DeleteGameObjectCommand());
            
            // 再生系
            AiCommandRegistry.Register(new Commands.EnterPlayModeCommand());
            AiCommandRegistry.Register(new Commands.ExitPlayModeCommand());
            AiCommandRegistry.Register(new Commands.IsPlayingCommand());

            // マテリアル系
            AiCommandRegistry.Register(new Commands.GetMaterialPropertiesCommand());
            AiCommandRegistry.Register(new Commands.SetMaterialPropertyCommand());

            // プレハブ系
            AiCommandRegistry.Register(new Commands.CreatePrefabCommand());
            AiCommandRegistry.Register(new Commands.ApplyPrefabInstanceCommand());

            // ユーザーコマンド登録
            UserCommandRegistry.Register(new UserCommands.ClearUserCommand());
            UserCommandRegistry.Register(new UserCommands.SaveContextUserCommand());
            UserCommandRegistry.Register(new UserCommands.LoadContextUserCommand());
            UserCommandRegistry.Register(new UserCommands.SummaryUserCommand());
            
            // ファイル管理コマンド
            UserCommandRegistry.Register(new UserCommands.FileCommands("BlockDirectory", "ディレクトリをアクセス拒否リストに追加", args => {
                if(args.Length > 0) FileSecurity.BlockDirectory(args[0]);
            }));
            UserCommandRegistry.Register(new UserCommands.FileCommands("UnblockDirectory", "ディレクトリをアクセス拒否リストから削除", args => {
                if(args.Length > 0) FileSecurity.UnblockDirectory(args[0]);
            }));
            UserCommandRegistry.Register(new UserCommands.FileCommands("AddFile", "コンテキストへファイル追加", _ => Debug.Log("AddFile")));
            UserCommandRegistry.Register(new UserCommands.FileCommands("RemoveFile", "コンテキストからファイル除外", _ => Debug.Log("RemoveFile")));
            UserCommandRegistry.Register(new UserCommands.FileCommands("ListFiles", "認識ファイル一覧表示", _ => Debug.Log("ListFiles")));
            UserCommandRegistry.Register(new UserCommands.FileCommands("RefreshFile", "ファイルを再読み込み", _ => Debug.Log("RefreshFile")));
            UserCommandRegistry.Register(new UserCommands.FileCommands("PinFile", "重要ファイルを優先保持", _ => Debug.Log("PinFile")));

            // AI実行制御コマンド
            UserCommandRegistry.Register(new UserCommands.ControlCommands("DryRun", "変更予定のみ表示", _ => Debug.Log("DryRun")));
            UserCommandRegistry.Register(new UserCommands.ControlCommands("Apply", "提案変更を適用", _ => Debug.Log("Apply")));
            UserCommandRegistry.Register(new UserCommands.ControlCommands("Revert", "AI変更を巻き戻す", _ => Debug.Log("Revert")));
            UserCommandRegistry.Register(new UserCommands.ControlCommands("ApprovePlan", "計画承認を要求", _ => Debug.Log("ApprovePlan")));
        }

        private void InitializeRightPanel()
        {
            _rightPanel.Clear();
            
            var tabContainer = new VisualElement();
            tabContainer.style.flexDirection = FlexDirection.Row;
            tabContainer.style.marginBottom = 5;
            tabContainer.style.flexShrink = 0; // レイアウト崩れ防止
            
            var btnCommands = new Button(() => ShowTab("commands")) { text = "Cmds" };
            var btnFiles = new Button(() => ShowTab("files")) { text = "Files" };
            
            tabContainer.Add(btnCommands);
            tabContainer.Add(btnFiles);
            _rightPanel.Add(tabContainer);

            _commandPanel = new ScrollView(ScrollViewMode.Vertical);
            _commandPanel.style.flexGrow = 1;
            _rightPanel.Add(_commandPanel);
            InitializeCommandPanel();

            _fileContextPanel = new ScrollView(ScrollViewMode.Vertical);
            _fileContextPanel.style.flexGrow = 1;
            _fileContextPanel.style.display = DisplayStyle.None;
            _rightPanel.Add(_fileContextPanel);
            InitializeFilePanel();
        }

        private void OnToggleCommandPanelClicked()
        {
            bool isVisible = _rightPanel.style.display == DisplayStyle.None;
            _rightPanel.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
            var button = rootVisualElement.Query<Button>().Where(b => b.text.Contains("Commands")).First();
            if (button != null) button.text = isVisible ? "<< Commands" : "Commands >>";
        }

        private void ShowTab(string tabName)
        {
            _commandPanel.style.display = tabName == "commands" ? DisplayStyle.Flex : DisplayStyle.None;
            _fileContextPanel.style.display = tabName == "files" ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void InitializeFilePanel()
        {
            _fileContextPanel.Clear();
            foreach (var item in _fileContexts)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 5;
                
                var toggle = new Toggle(item.FileName);
                toggle.value = item.IsEnabled;
                toggle.RegisterValueChangedCallback(evt => item.IsEnabled = evt.newValue);
                row.Add(toggle);
                
                var removeBtn = new Button(() => { _fileContexts.Remove(item); InitializeFilePanel(); }) { text = "X" };
                row.Add(removeBtn);
                
                _fileContextPanel.Add(row);
            }
        }

        private void SetUIEnabled(bool enabled)
        {
            _inputField.SetEnabled(enabled);
            _sendButton.SetEnabled(enabled);
            _stopButton.SetEnabled(!enabled);
        }

        private void OnStopClicked()
        {
            _cts?.Cancel();
            AddMessage("System", "通信を中断しました。", false, 0, 0, new Color(0.6f, 0.4f, 0.2f));
            SetUIEnabled(true);
        }

        private void OnFocus()
        {
            if (_settings == null || _client == null)
                return;

            var newSettings = GeminiSettings.Load();

            if (_settings.ApiKey != newSettings.ApiKey ||
                _settings.GetModelEnum() != newSettings.GetModelEnum())
            {
                _settings = newSettings;

                _client = new GeminiClient(
                    _settings.ApiKey,
                    _settings.GetModelEnum()
                );
            }
        }

        private void InitializeCommandPanel()
        {
            _commandPanel.Clear();
            var title = new Label("User Commands");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.8f, 0.8f, 0.8f);
            title.style.marginBottom = 10;
            _commandPanel.Add(title);

            foreach (var cmd in UserCommandRegistry.GetAllCommands())
            {
                var cmdContainer = new VisualElement();
                cmdContainer.style.marginBottom = 12;
                cmdContainer.style.paddingBottom = 8;
                cmdContainer.style.borderBottomWidth = 1;
                cmdContainer.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);

                var nameLabel = new Label(cmd.Name);
                nameLabel.style.fontSize = 12;
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLabel.style.color = new Color(0.3f, 0.6f, 1.0f);
                cmdContainer.Add(nameLabel);

                var descLabel = new Label(cmd.Description);
                descLabel.style.fontSize = 10;
                descLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                cmdContainer.Add(descLabel);

                _commandPanel.Add(cmdContainer);
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return && evt.shiftKey) return;
            if (evt.keyCode == KeyCode.Return)
            {
                OnSendClicked();
                evt.StopPropagation();
            }
        }

        private async void OnSendClicked()
        {
            string text = _inputField.value.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _inputField.value = "";

            if (text.StartsWith("/"))
            {
                var parts = text.Substring(1).Split(' ');
                string commandName = parts[0];
                string[] args = parts.Skip(1).ToArray();
                
                var cmd = UserCommandRegistry.GetAllCommands().FirstOrDefault(c => c.Name.Equals(commandName, System.StringComparison.OrdinalIgnoreCase));
                
                if (cmd != null)
                {
                    cmd.Execute(args);
                    string result = $"User command executed: {cmd.Name}";
                    Debug.Log(result);
                    AddMessage("System", result, false, 0, 0, new Color(0.2f, 0.5f, 0.2f));
                    return;
                }
            }

            AddMessage("You", text, true, 0, 0);
            await ProcessAiResponse(text, null, "user");
        }

        private async Task ProcessAiResponse(string text, string imagePath = null, string role = "user")
        {
            SetUIEnabled(false);
            _cts = new CancellationTokenSource();

            try
            {
                var response = await _client.SendAsync(text, role, imagePath, _fileContexts, _cts.Token);
                
                if (_cts.Token.IsCancellationRequested) return;

                AddMessage($"AI ({response.ModelName})", response.Message, false, response.PromptTokens, response.TotalTokens);
                _tokenLabel.text = $"Prompt: {response.PromptTokens} | Total: {response.TotalTokens}";
                
                AiCommandExecutor.Execute(response.Commands, async (result) => {
                    await HandleCommandResult(response, result);
                });
            }
            catch (System.OperationCanceledException)
            {
                AddMessage("System", "通信をキャンセルしました。", false, 0, 0, new Color(0.6f, 0.4f, 0.2f));
            }
            catch (System.Exception e)
            {
                if (!_cts.Token.IsCancellationRequested)
                    AddMessage("System", $"Error: {e.Message}", false, 0, 0, new Color(0.7f, 0.2f, 0.2f));
            }
            finally
            {
                if (!_cts.Token.IsCancellationRequested)
                {
                    SetUIEnabled(true);
                    _inputField.Focus();
                }
            }
        }

        private void ExecuteCommandsWithCheck(GeminiClient.GeminiResponse response)
        {
            var currentSettings = GeminiSettings.Load();
            var commands = response.Commands;

            if (currentSettings.EnableAutoExecuteCommands || 
                EditorUtility.DisplayDialog("AI Command Execution", 
                $"AIが {commands.Count} 件のコマンドを実行しようとしています。実行しますか？\n\n{string.Join("\n", commands.Select(c => c.name))}", "実行", "キャンセル"))
            {
                AiCommandExecutor.Execute(commands, async (result) => {
                    await HandleCommandResult(response, result);
                });
            }
        }

        private async Task HandleCommandResult(GeminiClient.GeminiResponse response, string result)
        {
            if (string.IsNullOrEmpty(result)) return;

            bool isError = result.Contains("Error:");
            
            AddMessage("System", isError ? $"コマンド実行失敗: {result}" : "Command results received.", false, 0, 0, isError ? new Color(0.6f, 0.2f, 0.2f) : new Color(0.2f, 0.2f, 0.2f));

            if (isError)
            {
                // セキュリティエラーの場合、特別なボタンを表示するロジック
                if (result.Contains("Access denied"))
                {
                    var fixButton = new Button(() => {
                        AddMessage("System", "このセキュリティ制限はプロジェクトの安全のためAssetsフォルダ内に限定されています。フォルダ構造を見直してください。", false, 0, 0);
                    }) { text = "セキュリティについて" };
                    _chatHistory.Add(fixButton);
                }
                else
                {
                    var retryButton = new Button(() => {
                        _ = ProcessAiResponse("前回のコマンドでエラーが発生しました。修正して再試行してください。", null, "system");
                    }) { text = "再試行を促す" };
                    _chatHistory.Add(retryButton);
                }
            }
            
            await Task.Delay(1000);

            string nextImagePath = null;
            if (response != null && response.Commands.Any(c => c.name == "CaptureGameView"))
            {
                nextImagePath = "Temp/AiCaptures/capture.png";
                await Task.Delay(1000);
            }

            await ProcessAiResponse(result, nextImagePath, "system");
        }

        public string ExportConversationJson()
        {
            return _client.ExportConversationJson();
        }

        public string GetFullHistoryText()
        {
            return string.Join("\n", _persistentHistory.Select(m => $"{m.sender}: {m.message}"));
        }

        public async Task<string> RequestSummary(string history)
        {
            // 要約プロンプトを投げて結果を返す
            var response = await _client.SendAsync($"以下の会話履歴を簡潔に要約してください。\n\n{history}", "user", null, _fileContexts, CancellationToken.None);
            return response.Message;
        }

        public void ClearConversation()
        {
            _persistentHistory.Clear();
            _chatHistory.Clear();
            _client.ClearConversation();
        }

        public void SetInitialContext(string context)
        {
            // 次回のSendAsync時にプロンプトの先頭に付与されるようClientを更新または履歴に追加
            _client.SetSystemInstruction(context);
        }

        public void AddMessage(string sender, string message, bool isUser, int promptTokens, int totalTokens, Color? bgColor = null)
        {
            _persistentHistory.Add(new ChatMessage { sender = sender, message = message, isUser = isUser, promptTokens = promptTokens, totalTokens = totalTokens });
            AddMessageInternal(sender, message, isUser, promptTokens, totalTokens, bgColor);
        }

        private void OnClearClicked()
        {
            ClearConversation();
            _tokenLabel.text = "Prompt: 0 | Total: 0";
            AddMessage("Gemini", "会話をリセットしました。", false, 0, 0);
        }

        private void AddMessageInternal(string sender, string message, bool isUser, int promptTokens, int totalTokens, Color? bgColor = null)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.marginBottom = 18;

            var bubble = new VisualElement();
            bubble.style.paddingTop = 12; bubble.style.paddingBottom = 12;
            bubble.style.paddingLeft = 16; bubble.style.paddingRight = 16;
            bubble.style.borderTopLeftRadius = 15; bubble.style.borderTopRightRadius = 15;
            bubble.style.borderBottomLeftRadius = 15; bubble.style.borderBottomRightRadius = 15;
            bubble.style.maxWidth = Length.Percent(85);

            if (isUser)
            {
                bubble.style.alignSelf = Align.FlexEnd;
                bubble.style.backgroundColor = bgColor ?? new Color(0.08f, 0.48f, 0.95f);
                bubble.style.borderBottomRightRadius = 2;
            }
            else
            {
                bubble.style.alignSelf = Align.FlexStart;
                bubble.style.backgroundColor = bgColor ?? new Color(0.24f, 0.24f, 0.24f);
                bubble.style.borderBottomLeftRadius = 2;
            }

            var senderLabel = new Label(sender);
            senderLabel.style.fontSize = 11;
            senderLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            senderLabel.style.color = new Color(0.75f, 0.75f, 0.75f);
            senderLabel.style.marginBottom = 6;
            bubble.Add(senderLabel);

            var messageLabel = new Label(message);
            messageLabel.selection.isSelectable = true;
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            messageLabel.style.fontSize = 14;
            messageLabel.style.color = Color.white;
            messageLabel.enableRichText = true;
            bubble.Add(messageLabel);

            if (totalTokens > 0)
            {
                var tokenLabel = new Label($"Prompt: {promptTokens} | Total: {totalTokens}");
                tokenLabel.style.fontSize = 9;
                tokenLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                tokenLabel.style.marginTop = 5;
                bubble.Add(tokenLabel);
            }

            container.Add(bubble);
            _chatHistory.Add(container);
            _chatHistory.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            _chatHistory.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _chatHistory.scrollOffset = new Vector2(0, _chatHistory.contentContainer.layout.height);
        }

        private void ApplyRootStyle(VisualElement element) { element.style.flexDirection = FlexDirection.Column; element.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f); }
        private void ApplyHeaderStyle(VisualElement element) { element.style.flexDirection = FlexDirection.Row; element.style.justifyContent = Justify.SpaceBetween; element.style.alignItems = Align.Center; element.style.paddingTop = 10; element.style.paddingBottom = 10; element.style.paddingLeft = 15; element.style.paddingRight = 15; element.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f); element.style.borderBottomWidth = 1; element.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f); element.style.flexShrink = 0; }
        private void ApplyHeaderButtonStyle(Button element) { element.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f); element.style.color = new Color(0.8f, 0.8f, 0.8f); element.style.borderTopWidth = 0; element.style.borderBottomWidth = 0; element.style.borderLeftWidth = 0; element.style.borderRightWidth = 0; element.style.borderTopLeftRadius = 4; element.style.borderTopRightRadius = 4; element.style.borderBottomLeftRadius = 4; element.style.borderBottomRightRadius = 4; element.style.paddingLeft = 8; element.style.paddingRight = 8; }
        private void ApplyInputAreaStyle(VisualElement element) { element.style.flexDirection = FlexDirection.Row; element.style.paddingTop = 15; element.style.paddingBottom = 15; element.style.paddingLeft = 15; element.style.paddingRight = 15; element.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f); element.style.borderTopWidth = 1; element.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f); element.style.flexShrink = 0; }
        private void ApplyTextFieldStyle(TextField element) { var textInput = element.Q("unity-text-input"); if (textInput != null) { textInput.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f); textInput.style.borderTopLeftRadius = 8; textInput.style.borderTopRightRadius = 8; textInput.style.borderBottomLeftRadius = 8; textInput.style.borderBottomRightRadius = 8; textInput.style.paddingLeft = 10; textInput.style.paddingRight = 10; textInput.style.paddingTop = 8; textInput.style.paddingBottom = 8; textInput.style.color = Color.white; textInput.style.borderTopWidth = 0; textInput.style.borderBottomWidth = 0; textInput.style.borderLeftWidth = 0; textInput.style.borderRightWidth = 0; } }
        private void ApplySendButtonStyle(Button element) { element.style.width = 80; element.style.height = 36; element.style.marginLeft = 12; element.style.backgroundColor = new Color(0.15f, 0.4f, 0.8f); element.style.color = Color.white; element.style.borderTopLeftRadius = 8; element.style.borderTopRightRadius = 8; element.style.borderBottomLeftRadius = 8; element.style.borderBottomRightRadius = 8; element.style.unityFontStyleAndWeight = FontStyle.Bold; element.style.borderTopWidth = 0; element.style.borderBottomWidth = 0; element.style.borderLeftWidth = 0; element.style.borderRightWidth = 0; }
        private void ApplyStopButtonStyle(Button element) { element.style.width = 80; element.style.height = 36; element.style.marginLeft = 12; element.style.backgroundColor = new Color(0.8f, 0.2f, 0.2f); element.style.color = Color.white; element.style.borderTopLeftRadius = 8; element.style.borderTopRightRadius = 8; element.style.borderBottomLeftRadius = 8; element.style.borderBottomRightRadius = 8; element.style.unityFontStyleAndWeight = FontStyle.Bold; element.style.borderTopWidth = 0; element.style.borderBottomWidth = 0; element.style.borderLeftWidth = 0; element.style.borderRightWidth = 0; }
        private void ApplyCommandPanelStyle(VisualElement element) { element.style.width = 250; element.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f); element.style.borderLeftWidth = 1; element.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f); element.style.paddingLeft = 10; element.style.paddingRight = 10; element.style.paddingTop = 10; element.style.display = DisplayStyle.None; }
        private void ApplyRightPanelStyle(VisualElement element) { element.style.width = 250; element.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f); element.style.borderLeftWidth = 1; element.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f); element.style.paddingLeft = 10; element.style.paddingRight = 10; element.style.paddingTop = 10; element.style.display = DisplayStyle.Flex; }
    }
}