// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class FindGameObjectsCommand : IAiCommand
    {
        public string Name => "FindGameObjects";
        public string Description => "FindGameObjects [NameFilter]";

        public string Execute(List<string> arguments)
        {
            string filter = arguments.Count > 0 ? arguments[0] : "";
            var gos = Object.FindObjectsOfType<GameObject>();
            var results = gos.Where(go => go.name.Contains(filter))
                             .Select(go => $"{go.name} (#{go.GetInstanceID()})")
                             .ToList();
            return results.Count > 0 ? string.Join("\n", results) : "No GameObjects found.";
        }
    }
}
#endif
