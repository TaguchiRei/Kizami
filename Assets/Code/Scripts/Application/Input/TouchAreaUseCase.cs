using System.Collections.Generic;
using UnityEngine;
using UsefulTools.Application.Runtime.Input;
using UsefulTools.Domain.Runtime;
using UsefulTools.Domain.Runtime.Input;

namespace UsefulTools.Application.Runtime
{
    public sealed class TouchAreaUseCase
    {
        private readonly ITouchAreaManagement _management;

        private readonly Dictionary<int, TouchSession> _sessions = new();

        private readonly Dictionary<string, TouchAreaGroup> _groups = new();

        public TouchAreaUseCase(ITouchAreaInputInfra infra, ITouchAreaManagement management)
        {
            _management = management;

            infra.OnTouchBegan += OnTouchBegan;
            infra.OnTouchMoved += OnTouchMoved;
            infra.OnTouchEnded += OnTouchEnded;
        }

        private void OnTouchBegan(TouchInputData input)
        {
            if (!_management.TryGetGroupName(input.ScreenPosition, out string groupName))
            {
                return;
            }

            if (!_groups.TryGetValue(groupName, out TouchAreaGroup group))
            {
                group = new TouchAreaGroup(groupName);

                _groups.Add(groupName, group);
            }

            if (!group.CanAcceptTouch())
            {
                return;
            }

            group.BeginTracking();

            var session = new TouchSession(input.TouchId, groupName, input.ScreenPosition);

            _sessions.Add(input.TouchId, session);

            _management.Press(groupName);
        }

        private void OnTouchMoved(TouchInputData input)
        {
            if (!_sessions.TryGetValue(input.TouchId, out TouchSession session))
            {
                return;
            }

            Vector2 delta = session.UpdatePosition(input.ScreenPosition);

            _management.Move(session.GroupName, delta);
        }

        private void OnTouchEnded(int touchId)
        {
            if (!_sessions.TryGetValue(touchId, out TouchSession session))
            {
                return;
            }

            _sessions.Remove(touchId);

            TouchAreaGroup group = _groups[session.GroupName];

            group.EndTracking();

            _management.Release(session.GroupName);
        }

        public void LateTick()
        {
            _management.LateTick();
        }
    }
}
