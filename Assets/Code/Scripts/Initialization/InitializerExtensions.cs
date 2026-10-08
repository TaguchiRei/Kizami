using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;
using Object = UnityEngine.Object;

namespace Kizami.Initialization
{
    /// <summary>
    /// Initializer が Inspector で受け取る参照を確かめる拡張。
    /// </summary>
    public static class InitializerExtensions
    {
        /// <summary>
        /// 参照が設定されているかを返す。設定されていなければエラーログを出す。
        /// </summary>
        /// <param name="initializer">エラーログの出力元</param>
        /// <param name="reference">確かめる参照</param>
        /// <param name="name">エラーログに出す参照の名前</param>
        public static bool IsAssigned(this InitializerBase initializer, Object reference, string name)
        {
            if (reference != null) return true;

            UsefulLogger.LogError($"{name} が設定されていません。", initializer);
            return false;
        }
    }
}
