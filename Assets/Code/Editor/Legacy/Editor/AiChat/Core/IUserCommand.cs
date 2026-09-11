// [Legacy] 作り直しに伴い全体を無効化
#if false
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
#endif
