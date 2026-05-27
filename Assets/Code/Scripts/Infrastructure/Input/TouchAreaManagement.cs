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

        private readonly PointerEventData _pointerEventData;

        private readonly List<RaycastResult> _results = new();

        private readonly Dictionary<string, Pointer> _devices = new();

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

            if (_results.Count == 0)
            {
                groupName = null;
                return false;
            }

            // 提供されたロジックに合わせ、最前面（index 0）のオブジェクトのみを対象とする
            GameObject target = _results[0].gameObject;

            if (target == null)
            {
                groupName = null;
                return false;
            }

            // TouchAreaコンポーネントがある場合はそのGroupNameを優先
            if (target.TryGetComponent(out TouchArea view))
            {
                groupName = view.GroupName;
                return true;
            }

            // タグによる判定（提供された最小構成ロジックへの互換性）
            if (target.CompareTag(TOUCH_AREA_TAG))
            {
                groupName = target.name; // タグのみの場合はオブジェクト名をGroupNameとする
                return true;
            }

            groupName = null;
            return false;
        }

        public void Press(string groupName)
        {
            Pointer device = GetOrCreateDevice(groupName);

            InputState.Change(device.press, 1f);
        }

        public void Release(string groupName)
        {
            if (!_devices.TryGetValue(groupName, out Pointer device))
            {
                return;
            }

            InputState.Change(device.press, 0f);

            InputState.Change(device.delta, Vector2.zero);
        }

        public void Move(string groupName, Vector2 delta)
        {
            Pointer device = GetOrCreateDevice(groupName);

            InputState.Change(device.delta, delta);
        }

        public void LateTick()
        {
            foreach (Pointer device in _devices.Values)
            {
                InputState.Change(device.delta, Vector2.zero);
            }
        }

        private Pointer GetOrCreateDevice(string groupName)
        {
            if (_devices.TryGetValue(groupName, out Pointer device))
            {
                return device;
            }

            device = (Pointer)InputSystem.AddDevice("Pointer", groupName);

            _devices.Add(groupName, device);

            return device;
        }
    }
}
