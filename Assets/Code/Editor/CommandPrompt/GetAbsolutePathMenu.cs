using UnityEngine;
using UnityEditor;
using System.IO;

public static class GetAbsolutePathMenu
{
    [MenuItem("Assets/AssetSupport/GetAbsolutePath", false, 20)]
    private static void CopyAbsolutePath()
    {
        Object obj = Selection.activeObject;
        if (obj == null) return;

        // Assetsからの相対パス取得
        string relativePath = AssetDatabase.GetAssetPath(obj);

        // プロジェクトのAssetsフォルダの絶対パス
        string dataPath = Application.dataPath;

        // "Assets" を削除して結合
        string fullPath = Path.Combine(
            dataPath.Substring(0, dataPath.Length - "Assets".Length),
            relativePath
        );

        // パス正規化（区切り文字対応）
        fullPath = Path.GetFullPath(fullPath);

        // クリップボードへコピー
        EditorGUIUtility.systemCopyBuffer = fullPath;

        Debug.Log("絶対パスをコピーしました: " + fullPath);
    }

    private static void CopyAbsoluteFolderPath()
    {
        Object obj = Selection.activeObject;
        if (obj == null) return;

        // Assetsからの相対パス取得
        string relativePath = AssetDatabase.GetAssetPath(obj);

        // プロジェクトのAssetsフォルダの絶対パス
        string dataPath = Application.dataPath;

        // "Assets" を削除して結合
        string fullPath = Path.Combine(
            dataPath.Substring(0, dataPath.Length - "Assets".Length),
            relativePath
        );

        // パス正規化（区切り文字対応）
        fullPath = Path.GetFullPath(fullPath);

        // クリップボードへコピー
        EditorGUIUtility.systemCopyBuffer = fullPath;

        Debug.Log("絶対パスをコピーしました: " + fullPath);
    }

    // メニューの有効/無効制御
    [MenuItem("Assets/AssetSupport/GetAbsolutePath", true)]
    private static bool ValidateCopyAbsolutePath()
    {
        return Selection.activeObject != null;
    }
}