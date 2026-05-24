using System.IO;
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor.Ai
{
    public static class AiGameViewCapture
    {
        public static void Capture()
        {
            string directory =
                "Temp/AiCaptures";

            Directory.CreateDirectory(directory);

            string path =
                $"{directory}/capture.png";

            ScreenCapture.CaptureScreenshot(path);

            Debug.Log(
                $"[AI] GameView captured: {path}");

            // ここでAI APIへ画像送信
            // 解析結果を次回prompt contextへ入れる
        }
    }
}