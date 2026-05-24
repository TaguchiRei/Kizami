using System.Collections.Generic;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class InvokeMenuItemCommand : IAiCommand
    {
        public string Name => "InvokeMenuItem";
        public string Description => "InvokeMenuItem [MenuPath]";

        public string Execute(List<string> arguments)
        {
            if (arguments == null || arguments.Count < 1)
                return "Error: Missing arguments. Expected 1 (MenuPath).";

            string menuPath = arguments[0];
            
            EditorApplication.ExecuteMenuItem(menuPath);
            return $"Invoked menu: {menuPath}";
        }
    }
}