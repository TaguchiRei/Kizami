// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class GetCompileErrorsCommand : IAiCommand
    {
        public string Name => "GetCompileErrors";
        public string Description => "GetCompileErrors";

        public string Execute(List<string> arguments)
        {
            var logs = AiConsoleLogStore.GetLastLogs(100).Where(l => l.Contains("[Error]") || l.Contains("[Exception]"));
            return logs.Any() ? "--- Current Errors ---\n" + string.Join("\n", logs) : "No errors found.";
        }
    }
}
#endif
