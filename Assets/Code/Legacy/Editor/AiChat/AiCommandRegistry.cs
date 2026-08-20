// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using System.Collections.Generic;
using System.Linq;

namespace UsefulTools.Editor.Ai
{
    public enum AiCommandType
    {
        Clear,
        SaveContext,
        LoadContext,
        AddFile,
        Summary,
        ListFiles,
        ListMemory,
        Remember,
        UndoContext
    }

    public static class AiCommandRegistry
    {
        private static readonly Dictionary<string, IAiCommand> _commands = new();

        public static void Register(IAiCommand command)
        {
            _commands[command.Name] = command;
        }

        public static bool Execute(string name, List<string> args, out string result)
        {
            if (_commands.TryGetValue(name, out var command))
            {
                result = command.Execute(args);
                return true;
            }
            result = $"Error: Unknown command '{name}'";
            return false;
        }

        public static bool TryExecute(string input, out string result)
        {
            result = string.Empty;
            if (string.IsNullOrEmpty(input) || !input.StartsWith("/")) return false;

            var parts = input.Substring(1).Split(' ', 2);
            var name = parts[0];
            var args = parts.Length > 1 ? parts[1].Split(' ').ToList() : new List<string>();

            return Execute(name, args, out result);
        }

        public static IEnumerable<IAiCommand> GetAllCommands()
        {
            return _commands.Values;
        }
    }
}
#endif
