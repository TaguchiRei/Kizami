using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace UsefulTools.Editor.Ai.Commands
{
    public class ClearContextCommand : IAiCommand
    {
        public string Name => "Clear";
        public string Description => "Clear context.";

        public string Execute(List<string> arguments)
        {
            // Note: UIのClearはAiChatWindowで直接制御されているため、
            // クリーンなモデル保持リセットのみを行う。
            return "Context cleared.";
        }
    }
}