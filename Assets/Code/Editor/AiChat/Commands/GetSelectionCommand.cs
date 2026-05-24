using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class GetSelectionCommand : IAiCommand
    {
        public string Name => "GetSelection";
        public string Description => "GetSelection";

        public string Execute(List<string> arguments)
        {
            var objs = Selection.objects;
            return objs.Length > 0 ? string.Join("\n", objs.Select(o => o.name)) : "Nothing selected.";
        }
    }
}