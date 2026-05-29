using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UsefulTools.Editor.Ai
{
    public static class GameObjectResolver
    {
        // HierarchyPath または #InstanceID を利用した安全な特定
        public static GameObject Resolve(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;

            // #ID 形式、または数値のみの場合
            string idStr = identifier.StartsWith("#") ? identifier.Substring(1) : identifier;
            if (int.TryParse(idStr, out int id))
            {
                var obj = UnityEditor.EditorUtility.InstanceIDToObject(id);
                if (obj is GameObject go) return go;
                if (obj is Component comp) return comp.gameObject;
            }

            // 1. パス探索
            var parts = identifier.Split('/');
            GameObject found = FindByPath(parts);
            if (found != null) return found;

            // 2. 名前による再帰的検索（現在のシーン全体）
            return FindByName(identifier);
        }

        private static GameObject FindByPath(string[] parts)
        {
            var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            GameObject current = null;
            foreach (var root in rootObjects)
            {
                if (root.name == parts[0])
                {
                    current = root;
                    break;
                }
            }

            if (current == null) return null;

            for (int i = 1; i < parts.Length; i++)
            {
                Transform child = current.transform.Find(parts[i]);
                if (child == null) return null;
                current = child.gameObject;
            }
            return current;
        }

        private static GameObject FindByName(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var found = FindChildRecursive(root.transform, name);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent.gameObject;
            foreach (Transform child in parent)
            {
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}