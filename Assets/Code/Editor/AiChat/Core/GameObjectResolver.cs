using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UsefulTools.Editor.Ai
{
    public static class GameObjectResolver
    {
        // HierarchyPathを利用した安全な特定 (例: "Root/Child/Target")
        public static GameObject Resolve(string hierarchyPath)
        {
            if (string.IsNullOrEmpty(hierarchyPath)) return null;

            var parts = hierarchyPath.Split('/');
            GameObject current = null;

            // 現在のアクティブなシーンのルートから検索
            var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            
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
    }
}