using System.Collections.Generic;
using System.Text;
using UnityEngine.SceneManagement;

namespace UsefulTools.Editor.Ai.Commands
{
    public class GetHierarchyCommand : IAiCommand
    {
        public string Name => "GetHierarchy";
        public string Description => "GetHierarchy";

        public string Execute(List<string> arguments)
        {
            var builder = new StringBuilder();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) 
                BuildHierarchy(root.transform, builder, 0);
            return builder.ToString();
        }

        private void BuildHierarchy(UnityEngine.Transform current, StringBuilder builder, int depth)
        {
            builder.Append(new string(' ', depth * 2)).AppendLine(current.name);
            foreach (UnityEngine.Transform child in current) BuildHierarchy(child, builder, depth + 1);
        }
    }
}