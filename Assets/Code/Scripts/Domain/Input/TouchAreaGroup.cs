using UnityEngine;

namespace UsefulTools.Domain.Runtime.Input
{
    public sealed class TouchAreaGroup
    {
        public string GroupName { get; }

        public bool IsTracking { get; private set; }

        public TouchAreaGroup(string groupName)
        {
            GroupName = groupName;
        }

        public bool CanAcceptTouch()
        {
            return !IsTracking;
        }

        public void BeginTracking()
        {
            IsTracking = true;
        }

        public void EndTracking()
        {
            IsTracking = false;
        }
    }
}
