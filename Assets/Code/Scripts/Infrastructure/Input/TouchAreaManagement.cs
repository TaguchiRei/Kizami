using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using UsefulTools.Application.Runtime.Input;

namespace UsefulTools.Infrastructure.Runtime
{
    public sealed class TouchAreaManagement : ITouchAreaManagement
    {
        private readonly GraphicRaycaster _raycaster;

        private readonly EventSystem _eventSystem;

        private readonly PointerEventData _pointerEventData;

        private readonly List<RaycastResult> _results = new();

        private readonly Dictionary<string, TouchAreaDevice> _devices = new();

        public TouchAreaManagement(GraphicRaycaster raycaster)
        {
            _raycaster = raycaster;

            _eventSystem = EventSystem.current;

            _pointerEventData = new PointerEventData(_eventSystem);

            InputSystem.RegisterLayout<TouchAreaDevice>();
        }

        public bool TryGetGroupId(Vector2 screenPosition, out string groupId)
        {
            _pointerEventData.position = screenPosition;

            _results.Clear();

            _raycaster.Raycast(_pointerEventData, _results);

            for (int i = 0; i < _results.Count; i++)
            {
                GameObject target = _results[i].gameObject;

                if (!target.TryGetComponent(out TouchArea view))
                {
                    continue;
                }

                groupId = new string(view.GroupId);

                return true;
            }

            groupId = null;

            return false;
        }

        public void Press(string groupId)
        {
            TouchAreaDevice device = GetOrCreateDevice(groupId);

            InputState.Change(device.Press, true);
        }

        public void Release(string groupId)
        {
            if (!_devices.TryGetValue(groupId, out TouchAreaDevice device))
            {
                return;
            }

            InputState.Change(device.Press, false);

            InputState.Change(device.Delta, Vector2.zero);
        }

        public void Move(string groupId, Vector2 delta)
        {
            TouchAreaDevice device = GetOrCreateDevice(groupId);

            InputState.Change(device.Delta, delta);
        }

        public void LateTick()
        {
            foreach (TouchAreaDevice device in _devices.Values)
            {
                InputState.Change(device.Delta, Vector2.zero);
            }
        }

        private TouchAreaDevice GetOrCreateDevice(string groupId)
        {
            if (_devices.TryGetValue(groupId, out TouchAreaDevice device))
            {
                return device;
            }

            device = InputSystem.AddDevice<TouchAreaDevice>();

            _devices.Add(groupId, device);

            return device;
        }
    }
}