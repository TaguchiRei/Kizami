// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEngine;

namespace UsefulTools.Editor.Ai.UserCommands
{
    public class FileCommands : IUserCommand
    {
        public string Name { get; }
        public string Description { get; }
        private readonly System.Action<string[]> _action;

        public FileCommands(string name, string description, System.Action<string[]> action)
        {
            Name = name;
            Description = description;
            _action = action;
        }

        public void Execute(string[] args) => _action?.Invoke(args);
    }
}
#endif
