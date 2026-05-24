using UnityEngine;
using UnityEditor;

namespace UsefulTools.Editor.Ai.UserCommands
{
    public class ControlCommands : IUserCommand
    {
        public string Name { get; }
        public string Description { get; }
        private readonly System.Action<string[]> _action;

        public ControlCommands(string name, string description, System.Action<string[]> action)
        {
            Name = name;
            Description = description;
            _action = action;
        }

        public void Execute(string[] args) => _action?.Invoke(args);
    }
}