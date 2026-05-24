using System.Collections.Generic;
using System.Linq;

namespace UsefulTools.Editor.Ai
{
    public static class UserCommandRegistry
    {
        private static readonly List<IUserCommand> _commands = new List<IUserCommand>();

        public static void Register(IUserCommand command) => _commands.Add(command);
        public static IEnumerable<IUserCommand> GetAllCommands() => _commands;
    }
}