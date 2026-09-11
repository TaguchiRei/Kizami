// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor.Ai
{
    public static class AiCommandExecutor
    {
        public static void Execute(IEnumerable<GeminiClient.GeminiCommand> commands, Action<string> onResultCalculated = null)
        {
            if (commands == null || !commands.Any()) return;

            var actions = new List<PendingCommand>();
            foreach (var cmd in commands)
            {
                var args = cmd.arguments ?? new List<string>();
                
                // Registry経由でコマンド実行を委譲するラッパーを作成
                actions.Add(new PendingCommand($"{cmd.name}: {string.Join(", ", args.Take(1))}{(args.Count > 1 ? "..." : "")}", () => 
                {
                    if (AiCommandRegistry.Execute(cmd.name, args, out var result))
                    {
                        return result;
                    }
                    return $"Error: Unknown command '{cmd.name}'";
                }));
            }

            EnqueueAndExecute(actions, onResultCalculated);
        }

        private static void EnqueueAndExecute(List<PendingCommand> actions, Action<string> onResultCalculated)
        {
            if (actions.Count == 0) return;

            var settings = GeminiSettings.Load();
            
            // trueなら自動実行、falseなら確認を挟む
            bool shouldAutoExecute = settings.EnableAutoExecuteCommands;
            
            if (!shouldAutoExecute)
            {
                string preview = string.Join("\n", actions.Select(a => "- " + a.Preview));
                if (EditorUtility.DisplayDialog("AI Command Confirmation", $"AIが{actions.Count}件のコマンドを実行しようとしています。\n承認しますか？\n\n{preview}", "Approve", "Reject"))
                {
                    ExecuteActions(actions, onResultCalculated);
                }
                else
                {
                    onResultCalculated?.Invoke("Error: Command execution rejected by user.");
                }
            }
            else
            {
                ExecuteActions(actions, onResultCalculated);
            }
        }

        private static void ExecuteActions(List<PendingCommand> actions, Action<string> onResultCalculated)
        {
            var sb = new StringBuilder();
            
            if (EditorApplication.isPlaying)
            {
                sb.AppendLine("> **Warning: Unity is currently in Play Mode. Any changes made to the Scene or Components will be lost when Play Mode is exited.**");
                sb.AppendLine();
            }

            sb.AppendLine("### Command Execution Results");
            foreach (var command in actions)
            {
                sb.AppendLine($"#### {command.Preview}");
                try { sb.AppendLine(command.Execute?.Invoke() ?? "Success (No output)"); }
                catch (Exception e) { sb.AppendLine($"Error: {e.Message}"); Debug.LogException(e); }
            }
            onResultCalculated?.Invoke(sb.ToString());
        }

        private sealed class PendingCommand
        {
            public string Preview;
            public Func<string> Execute;
            public PendingCommand(string preview, Func<string> execute) { Preview = preview; Execute = execute; }
        }
    }
}
#endif
