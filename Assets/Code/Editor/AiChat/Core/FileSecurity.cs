using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;

namespace UsefulTools.Editor.Ai
{
    public static class FileSecurity
    {
        private static readonly string RootPath = Path.GetFullPath("Assets");
        private static readonly HashSet<string> Blacklist = new HashSet<string>();

        public static void ClearBlacklist()
        {
            Debug.Log($"Clearing Blacklist: {Blacklist.Count} items removed.");
            Blacklist.Clear();
        }

        public static void PrintBlacklist()
        {
            Debug.Log($"Current Blacklist Items: {string.Join(", ", Blacklist)}");
        }

        public static void BlockDirectory(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!Blacklist.Contains(fullPath))
                Blacklist.Add(fullPath);
        }

        public static void UnblockDirectory(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (Blacklist.Contains(fullPath))
                Blacklist.Remove(fullPath);
        }

        public static bool IsAccessAllowed(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            
            string absolutePath = Path.GetFullPath(path);
            
            // Assets配下であるか確認
            if (!absolutePath.StartsWith(RootPath)) return false;

            // ブラックリストにないか確認
            return !Blacklist.Any(b => absolutePath.StartsWith(b));
        }

        public static string GetSecurityError(string path)
        {
            return $"Error: Access denied to path '{path}'. This directory is restricted.";
        }
    }
}