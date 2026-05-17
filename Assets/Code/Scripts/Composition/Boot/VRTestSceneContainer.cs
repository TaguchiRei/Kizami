using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRTest.Composition.Runtime.Boot
{
    [DefaultExecutionOrder(-1000)]
    public sealed class VRTestSceneContainer : MonoBehaviour
    {
        private static VRTestSceneContainer _instance;

        private readonly Dictionary<Type, object> _container = new();

        private void Awake()
        {
            _instance = this;
        }

        public static void Register<T>(T instance)
        {
            Debug.Log($"Register{typeof(T).Name}");
            var type = typeof(T);

            if (_instance._container.ContainsKey(type))
            {
                Debug.LogWarning($"{type.Name} already registered.");
                return;
            }

            _instance._container.Add(type, instance);
        }

        public bool TryGet<T>(out T result)
        {
            if (_container.TryGetValue(typeof(T), out var value))
            {
                result = (T)value;
                return true;
            }

            result = default;
            return false;
        }
    }
}