using Kizami.External;
using UnityEditor;

namespace Kizami.EditorTools
{
    /// <summary>
    /// 保存データ（進行とコンフィグ）を消すエディタのメニュー。
    /// </summary>
    public static class SaveDataMenu
    {
        private const string MENU_PATH = "Kizami/Delete Save";

        [MenuItem(MENU_PATH)]
        private static void DeleteSave()
        {
            if (!EditorUtility.DisplayDialog("Delete Save", $"保存データを消します。\n{SaveDirectory.Path}",
                    "Delete", "Cancel")) return;

            SaveDirectory.DeleteAll();
        }
    }
}
