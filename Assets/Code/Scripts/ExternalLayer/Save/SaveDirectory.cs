using System.IO;

namespace Kizami.External
{
    /// <summary>
    /// 保存データ（JsonSaveStore のファイル）を置くフォルダ。
    /// </summary>
    public static class SaveDirectory
    {
        private const string DIRECTORY_NAME = "Save";

        /// <summary> 保存データを置くフォルダのパス </summary>
        public static string Path => System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, DIRECTORY_NAME);

        /// <summary>
        /// 保存データをフォルダごと消す。
        /// </summary>
        public static void DeleteAll()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
