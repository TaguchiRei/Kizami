// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEngine;

namespace UsefulTools.Editor.Ai.UserCommands
{
    public class ClearUserCommand : IUserCommand
    {
        public string Name => "Clear";
        public string Description => "会話履歴を初期化します";
        public void Execute(string[] args)
        {
            Debug.Log("User Command: Clear executed.");
        }
    }
}
#endif
