// [Legacy] 作り直しに伴い全体を無効化
#if false
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UsefulTools.Editor.Ai
{
    public static class AiConsoleLogStore
    {
        private static readonly Queue<string> Logs = new();

        [UnityEditor.InitializeOnLoadMethod]
        private static void Initialize()
        {
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(
            string condition,
            string stackTrace,
            LogType type)
        {
            Logs.Enqueue(
                $"[{type}] {condition}");

            while (Logs.Count > 30)
            {
                Logs.Dequeue();
            }
        }

        public static List<string> GetLastLogs(int count)
        {
            return Logs.Reverse().Take(count).Reverse().ToList();
        }
    }
}
#endif
