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
        }

        public bool TryGetGroupName(Vector2 screenPosition, out string groupName)
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

                groupName = new string(view.GroupName);

                return true;
            }

            groupName = null;

            return false;
        }

        public void Press(string groupName)
        {
            TouchAreaDevice device = GetOrCreateDevice(groupName);

            InputState.Change(device.Press, true);
        }

        public void Release(string groupName)
        {
            if (!_devices.TryGetValue(groupName, out TouchAreaDevice device))
            {
                return;
            }

            InputState.Change(device.Press, false);

            InputState.Change(device.Delta, Vector2.zero);
        }

        public void Move(string groupName, Vector2 delta)
        {
            TouchAreaDevice device = GetOrCreateDevice(groupName);

            InputState.Change(device.Delta, delta);
        }

        public void LateTick()
        {
            foreach (TouchAreaDevice device in _devices.Values)
            {
                InputState.Change(device.Delta, Vector2.zero);
            }
        }

        private TouchAreaDevice GetOrCreateDevice(string groupName)
        {
            if (_devices.TryGetValue(groupName, out TouchAreaDevice device))
            {
                return device;
            }

            device = InputSystem.AddDevice<TouchAreaDevice>();

            _devices.Add(groupName, device);

            return device;
        }
    }
}
