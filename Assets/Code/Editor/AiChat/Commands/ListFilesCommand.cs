using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UsefulTools.Editor.Ai.Commands
{
    public class ListFilesCommand : IAiCommand
    {
        public string Name => "ListFiles";
        public string Description => "ListFiles [Path]";

        public string Execute(List<string> arguments)
        {
            string path = arguments.Count > 0 ? arguments[0] : "Assets";
            if (!Directory.Exists(path)) return $"Error: Path {path} not found.";
            
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            return "Files:\n" + string.Join("\n", files.Take(20));
        }
    }
}