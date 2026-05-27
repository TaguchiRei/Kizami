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
        private const string TOUCH_AREA_TAG = "TouchArea";

        private readonly GraphicRaycaster _raycaster;

        private readonly EventSystem _eventSystem;

        private readonly List<RaycastResult> _results = new();

        private readonly Dictionary<string, VirtualPointerDevice> _devices = new();

        public TouchAreaManagement(GraphicRaycaster raycaster)
        {
            _raycaster = raycaster;
            _eventSystem = EventSystem.current;
        }

        public bool TryGetGroupName(Vector2 screenPosition, out string groupName)
        {
            PointerEventData pointerEventData = new(_eventSystem)
            {
                position = screenPosition
            };

            _results.Clear();

            _raycaster.Raycast(pointerEventData, _results);

            for (int i = 0; i < _results.Count; i++)
            {
                GameObject target = _results[i].gameObject;

                if (target == null)
                {
                    continue;
                }

                if (target.TryGetComponent(out TouchArea view))
                {
                    groupName = view.GroupName;
                    return true;
                }

                if (target.CompareTag(TOUCH_AREA_TAG))
                {
                    groupName = target.name;
                    return true;
                }
            }

            groupName = null;
            return false;
        }

        public void Press(string groupName, Vector2 position)
        {
            VirtualPointerDevice device = GetOrCreateDevice(groupName);

            device.Position = position;
            device.Delta = Vector2.zero;
            device.IsPressed = true;

            InputState.Change(device.Pointer.position, device.Position);
            InputState.Change(device.Pointer.delta, device.Delta);
        }

        public void Move(string groupName, Vector2 delta)
        {
            if (!_devices.TryGetValue(groupName, out VirtualPointerDevice device))
            {
                return;
            }

            device.Delta = delta;
            device.Position += delta;

            InputState.Change(device.Pointer.position, device.Position);
            InputState.Change(device.Pointer.delta, device.Delta);
        }

        public void Release(string groupName)
        {
            if (!_devices.TryGetValue(groupName, out VirtualPointerDevice device))
            {
                return;
            }

            device.IsPressed = false;
            device.Delta = Vector2.zero;

            InputState.Change(device.Pointer.delta, Vector2.zero);
        }

        public void LateTick()
        {
            foreach (VirtualPointerDevice device in _devices.Values)
            {
                if (device.Delta == Vector2.zero)
                {
                    continue;
                }

                device.Delta = Vector2.zero;

                InputState.Change(device.Pointer.delta, Vector2.zero);
            }
        }

        private VirtualPointerDevice GetOrCreateDevice(string groupName)
        {
            if (_devices.TryGetValue(groupName, out VirtualPointerDevice device))
            {
                return device;
            }

            Pointer pointer = (Pointer)InputSystem.AddDevice("Pointer");

            InputSystem.SetDeviceUsage(pointer, groupName);

            device = new VirtualPointerDevice(pointer);

            _devices.Add(groupName, device);

            return device;
        }

        private sealed class VirtualPointerDevice
        {
            public Pointer Pointer { get; }

            public Vector2 Position { get; set; }

            public Vector2 Delta { get; set; }

            public bool IsPressed { get; set; }

            public VirtualPointerDevice(Pointer pointer)
            {
                Pointer = pointer;
            }
        }
    }
}