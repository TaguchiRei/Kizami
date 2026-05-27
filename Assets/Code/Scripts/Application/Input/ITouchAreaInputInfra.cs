using System;
using UsefulTools.Domain.Runtime;

namespace UsefulTools.Application.Runtime.Input
{
    public interface ITouchAreaInputInfra
    {
        event Action<TouchInputData> OnTouchBegan;

        event Action<TouchInputData> OnTouchMoved;

        event Action<int> OnTouchEnded;
    }
}