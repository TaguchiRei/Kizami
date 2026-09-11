// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;

namespace UsefulTools.Infrastructure.Runtime.Input
{
    public interface IInputSource<T> where T : unmanaged
    {
        public void RegisterAction(Action<InputContext<T>> input);

        public void UnRegisterAction(Action<InputContext<T>> input);
    }
}
#endif
