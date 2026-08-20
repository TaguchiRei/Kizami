// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UsefulTools.Composition.Runtime.Boot
{
    public sealed class InGameContainer : MonoBehaviour
    {
        private static InGameContainer _instance;

        public static InGameContainer Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<InGameContainer>();
                    if (_instance == null)
                    {
                        var go = new GameObject("InGameContainer");
                        _instance = go.AddComponent<InGameContainer>();
                    }
                }
                return _instance;
            }
        }

        private readonly Dictionary<Type, object> _instances = new();

        public static void Register<T>(T instance)
        {
            var type = typeof(T);

            if (Instance._instances.ContainsKey(type))
            {
                Debug.LogWarning($"{type.Name} already registered.");
                return;
            }

            Instance._instances.Add(type, instance);
        }

        public bool TryGet<T>(out T result)
        {
            if (_instances.TryGetValue(typeof(T), out var value))
            {
                result = (T)value;
                return true;
            }

            result = default;
            return false;
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }
    }
}
#endif
