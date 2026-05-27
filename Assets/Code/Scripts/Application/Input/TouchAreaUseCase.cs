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

        public TouchAreaUseCase(
            ITouchAreaInputInfra infra,
            ITouchAreaManagement management)
        {
            _management = management;

            infra.OnTouchBegan += OnTouchBegan;
            infra.OnTouchMoved += OnTouchMoved;
            infra.OnTouchEnded += OnTouchEnded;
        }

        private void OnTouchBegan(TouchInputData input)
        {
            if (!_management.TryGetGroupName(
                    input.ScreenPosition,
                    out string groupName))
            {
                return;
            }

            Debug.Log(
                $"[TouchAreaUseCase] Area Hit: {groupName} at {input.ScreenPosition}");

            if (!_groups.TryGetValue(groupName, out TouchAreaGroup group))
            {
                group = new TouchAreaGroup(groupName);

                _groups.Add(groupName, group);
            }

            if (!group.CanAcceptTouch())
            {
                Debug.LogWarning(
                    $"[TouchAreaUseCase] Area {groupName} cannot accept more touches.");

                return;
            }

            group.BeginTracking();

            TouchSession session = new(
                input.TouchId,
                groupName,
                input.ScreenPosition);

            _sessions.Add(input.TouchId, session);

            _management.Press(
                groupName,
                input.ScreenPosition);

            Debug.Log(
                $"[TouchAreaUseCase] Session Started: Area={groupName}, TouchId={input.TouchId}");
        }

        private void OnTouchMoved(TouchInputData input)
        {
            if (!_sessions.TryGetValue(
                    input.TouchId,
                    out TouchSession session))
            {
                return;
            }

            Vector2 delta = session.UpdatePosition(input.ScreenPosition);

            Debug.Log(
                $"[TouchAreaUseCase] Moved: {session.GroupName}, delta={delta}");

            _management.Move(
                session.GroupName,
                delta);
        }

        private void OnTouchEnded(int touchId)
        {
            if (!_sessions.TryGetValue(
                    touchId,
                    out TouchSession session))
            {
                return;
            }

            _sessions.Remove(touchId);

            TouchAreaGroup group = _groups[session.GroupName];

            group.EndTracking();

            _management.Release(session.GroupName);

            Debug.Log(
                $"[TouchAreaUseCase] Session Ended: Area={session.GroupName}, TouchId={touchId}");
        }

        public void LateTick()
        {
            _management.LateTick();
        }
    }
}