// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using System.Collections.Generic;

namespace UsefulTools.Editor.Ai
{
    public interface IAiCommand
    {
        string Name { get; }
        string Description { get; }
        string Execute(List<string> arguments);
    }
}
#endif
