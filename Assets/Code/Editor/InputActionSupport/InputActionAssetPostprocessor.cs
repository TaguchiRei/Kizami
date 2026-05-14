using UnityEditor;
using UnityEngine.InputSystem;

namespace UsefulTools.Editor
{
    /// <summary>
    /// .inputactions ファイルの変更を検知し、enumの自動生成をトリガーする
    /// </summary>
    public class InputActionAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (InputSupportTool.Timing == GenerateTiming.None) return;

            bool needsRefresh = false;
            foreach (string path in importedAssets)
            {
                if (path.EndsWith(".inputactions"))
                {
                    InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                    if (asset != null)
                    {
                        if (InputActionEnumGenerator.Generate(asset))
                        {
                            needsRefresh = true;
                        }
                    }
                }
            }

            if (needsRefresh)
            {
                AssetDatabase.Refresh();
            }
        }
    }
}
