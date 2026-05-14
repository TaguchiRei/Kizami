using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 
/// </summary>
public static class ProjectWindowSelector
{
    /// <summary>
    /// 現在選択しているフォルダのパスを取得する
    /// </summary>
    /// <returns></returns>
    public static string GetSelectedFolderPath()
    {
        var objs = Selection.GetFiltered<Object>(SelectionMode.Assets);
        if (objs.Length == 0)
        {
            return "Assets";
        }

        string path = AssetDatabase.GetAssetPath(objs[0]);

        if (File.Exists(path))
        {
            return Path.GetDirectoryName(path);
        }

        return path;
    }

    /// <summary>
    /// 指定したアセットを選択した状態にする
    /// </summary>
    /// <param name="assetPath"></param>
    public static void SelectAsset(string assetPath)
    {
        if (!File.Exists(assetPath) && !Directory.Exists(assetPath))
        {
            Debug.LogWarning($"指定パスが存在しません: {assetPath}");
            return;
        }

        var obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
        Selection.activeObject = obj;
        EditorGUIUtility.PingObject(obj);
    }
}