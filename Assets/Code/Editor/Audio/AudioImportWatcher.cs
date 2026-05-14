using UnityEditor;
using UnityEngine;
using UsefulTools.Editor;
using System.Collections.Generic;
using System.Linq;

public class AudioImportWatcher : AssetPostprocessor
{
    private static HashSet<string> _processingPaths = new HashSet<string>();

    // 全てのアセットインポート完了時に一括で呼ばれる
    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        // 今回の処理（リネーム等）によるインポートは無視
        var newImports = importedAssets.Where(path => !_processingPaths.Contains(path)).ToList();
        
        // オーディオファイルのみを抽出（拡張子で判別）
        var audioPaths = newImports.Where(path => 
            path.EndsWith(".mp3", System.StringComparison.OrdinalIgnoreCase) || 
            path.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase) || 
            path.EndsWith(".ogg", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".aiff", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".m4a", System.StringComparison.OrdinalIgnoreCase)
        ).ToList();

        if (audioPaths.Count > 0)
        {
            // 名前が英語・数字・_以外を含む、または重複しているものだけを抽出
            var filteredPaths = audioPaths.Where(path => IsInvalidName(path) || IsDuplicate(path)).ToList();
            
            if (filteredPaths.Count > 0)
            {
                // インポートされたファイルをキューに渡してウィンドウを表示
                AudioImportTab.ShowWindow(filteredPaths);
            }
        }

        // 処理済みリストから今回移動・追加されたものを消す（次回の通常インポートに備える）
        foreach (var path in importedAssets) _processingPaths.Remove(path);
        foreach (var path in movedAssets) _processingPaths.Remove(path);
    }

    private static bool IsInvalidName(string path)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        return !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_]+$");
    }

    private static bool IsDuplicate(string path)
    {
        string nameWithExt = System.IO.Path.GetFileName(path);
        string bgmPath = (AudioSupportTool.BGMFolder.EndsWith("/") ? AudioSupportTool.BGMFolder : AudioSupportTool.BGMFolder + "/") + nameWithExt;
        string sePath = (AudioSupportTool.SEFolder.EndsWith("/") ? AudioSupportTool.SEFolder : AudioSupportTool.SEFolder + "/") + nameWithExt;
        
        // 元のパスと違う場合に重複チェック（無限ループ防止）
        if (path == bgmPath || path == sePath) return false;

        return AssetDatabase.LoadAssetAtPath<Object>(bgmPath) != null || AssetDatabase.LoadAssetAtPath<Object>(sePath) != null;
    }

    public static void MarkAsProcessing(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        _processingPaths.Add(path.Replace("\\", "/"));
    }
}
