using System.Collections.Generic;

namespace UsefulTools.Editor.Ai
{
    public interface IUserCommand
    {
        string Name { get; }
        string Description { get; }
        void Execute(string[] args);
    }
}