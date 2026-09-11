// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using UnityEngine;
using UsefulAttribute;

namespace UsefulTools.UtilityUnity.Runtime.UtilityUnity
{
    [DefaultExecutionOrder(100)]
    public abstract class InitializerBase : MonoBehaviour, IComparable<InitializerBase>
    {
        public int InitializationOrder = 0;
        [ShowOnly] public bool Initialized { get; protected set; } = false;

        public virtual void Initialize()
        {
            if (Initialized) return;

            Initialized = true;
        }

        public int CompareTo(InitializerBase other)
        {
            return InitializationOrder.CompareTo(other.InitializationOrder);
        }
    }
}
#endif
