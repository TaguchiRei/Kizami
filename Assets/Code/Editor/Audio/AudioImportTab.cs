using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UsefulTools.Editor;

public class AudioImportTab : EditorWindow
{
    public enum AudioCategory { BGM, SE }

    private class ImportItem
    {
        public AudioClip Clip;
        public string Path;
        public string NewName;
        public AudioCategory Category;
    }

    private List<ImportItem> _items = new List<ImportItem>();
    private int _currentIndex = 0;
    private Vector2 _scrollPos;

    public static void ShowWindow(List<string> paths)
    {
        var window = GetWindow<AudioImportTab>("Audio Import Tool");
        window.minSize = new Vector2(400, 300);

        bool added = false;
        foreach (var path in paths)
        {
            if (window._items.Any(i => i.Path == path)) continue;

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;

            AudioCategory initialCategory = AudioCategory.SE;

            if (AudioSupportTool.AutoCategoryEnabled)
            {
                initialCategory = (clip.length >= AudioSupportTool.AutoCategoryThreshold) ? AudioCategory.BGM : AudioCategory.SE;
            }
            else
            {
                string lowerPath = path.ToLower().Replace("\\", "/");
                if (lowerPath.Contains("/bgm/")) initialCategory = AudioCategory.BGM;
                else if (lowerPath.Contains("/se/")) initialCategory = AudioCategory.SE;
                else initialCategory = (clip.length >= AudioSupportTool.AutoCategoryThreshold) ? AudioCategory.BGM : AudioCategory.SE;
            }

            window._items.Add(new ImportItem
            {
                Clip = clip,
                Path = path,
                NewName = Path.GetFileNameWithoutExtension(path),
                Category = initialCategory
            });
            added = true;
        }
        
        if (added)
        {
            window.Show();
            window.Focus();
        }
    }

    private void OnGUI()
    {
        if (_items == null || _items.Count == 0 || _currentIndex >= _items.Count)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
            {
                EditorGUILayout.HelpBox("全ての処理が完了しました。", MessageType.Info);
                if (GUILayout.Button("Close", GUILayout.Height(30))) Close();
            }
            return;
        }

        var current = _items[_currentIndex];

        using (var scroll = new EditorGUILayout.ScrollViewScope(_scrollPos))
        {
            _scrollPos = scroll.scrollPosition;
            using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
            {
                EditorGUILayout.LabelField($"Audio Import Queue: {_currentIndex + 1} / {_items.Count}", EditorStyles.boldLabel);
                
                EditorGUILayout.Space();

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Target Clip", current.Clip.name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Current Path", current.Path, EditorStyles.miniLabel);

                    EditorGUILayout.Space();

                    current.NewName = EditorGUILayout.TextField("New File Name", current.NewName);
                    
                    bool invalid = IsInvalidName(current.NewName);
                    if (invalid)
                    {
                        EditorGUILayout.HelpBox("英語・数字・_ 以外の文字が含まれています。適用してもフォルダ分けされません。", MessageType.Warning);
                    }

                    current.Category = (AudioCategory)EditorGUILayout.EnumPopup("Category", current.Category);

                    // 重複チェック
                    string targetFolder = (current.Category == AudioCategory.BGM) ? AudioSupportTool.BGMFolder : AudioSupportTool.SEFolder;
                    string extension = Path.GetExtension(current.Path);
                    string targetPath = (targetFolder.EndsWith("/") ? targetFolder : targetFolder + "/") + current.NewName + extension;
                    bool exists = AssetDatabase.LoadAssetAtPath<Object>(targetPath) != null && current.Path != targetPath;

                    if (exists)
                    {
                        EditorGUILayout.HelpBox("同名のファイルが既に存在します。", MessageType.Error);
                    }

                    EditorGUILayout.Space();

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (exists)
                        {
                            if (GUILayout.Button("Overwrite", GUILayout.Height(30)))
                            {
                                if (ApplyCurrent(true)) _currentIndex++;
                                if (_currentIndex >= _items.Count) FinalizeImport();
                            }
                            if (GUILayout.Button("Rename (Unique)", GUILayout.Height(30)))
                            {
                                if (ApplyCurrent(false)) _currentIndex++;
                                if (_currentIndex >= _items.Count) FinalizeImport();
                            }
                        }
                        else
                        {
                            if (GUILayout.Button("Apply & Next", GUILayout.Height(30)))
                            {
                                if (ApplyCurrent(false)) _currentIndex++;
                                if (_currentIndex >= _items.Count) FinalizeImport();
                            }
                        }

                        if (GUILayout.Button("Cancel / Delete", GUILayout.Height(30)))
                        {
                            if (EditorUtility.DisplayDialog("Confirm", "このアセットを削除してインポートをキャンセルしますか？", "Delete", "Skip"))
                            {
                                AssetDatabase.DeleteAsset(current.Path);
                            }
                            _currentIndex++;
                            if (_currentIndex >= _items.Count) FinalizeImport();
                        }
                    }
                }
                
                EditorGUILayout.Space(10);
                
                if (GUILayout.Button("Apply All Remaining"))
                {
                    if (EditorUtility.DisplayDialog("Confirm", "残りの全ファイルをインポートしますか？（不正な名前のファイルは移動されません。重複はユニーク名にリネームされます）", "Yes", "No"))
                    {
                        ApplyAllRemaining();
                    }
                }
            }
        }
    }

    private bool ApplyCurrent(bool overwrite = false)
    {
        var current = _items[_currentIndex];
        
        // 英語・数字・_ 以外が含まれている場合は移動させない
        if (IsInvalidName(current.NewName))
        {
            Debug.LogWarning($"[UsefulTools] Invalid name '{current.NewName}' contains characters other than Alphanumeric or Underscore. Skipping folder organization.");
            return true;
        }

        string targetFolder = (current.Category == AudioCategory.BGM) ? AudioSupportTool.BGMFolder : AudioSupportTool.SEFolder;
        EnsureFolderExists(targetFolder);
        AssetDatabase.Refresh();

        return MoveAsset(current, targetFolder, overwrite);
    }

    private bool MoveAsset(ImportItem item, string targetFolder, bool overwrite)
    {
        string extension = Path.GetExtension(item.Path);
        string newPath = (targetFolder.EndsWith("/") ? targetFolder : targetFolder + "/") + item.NewName + extension;
        newPath = newPath.Replace("\\", "/");

        if (item.Path == newPath) return true;

        if (AssetDatabase.LoadAssetAtPath<Object>(newPath) != null)
        {
            if (overwrite)
            {
                AssetDatabase.DeleteAsset(newPath);
            }
            else
            {
                newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);
            }
        }

        AudioImportWatcher.MarkAsProcessing(newPath);
        AudioImportWatcher.MarkAsProcessing(item.Path);

        string error = AssetDatabase.MoveAsset(item.Path, newPath);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"[UsefulTools] Failed to move: {error} (Target: {newPath})");
            return false;
        }
        else
        {
            item.Path = newPath;
            return true;
        }
    }

    private void EnsureFolderExists(string folderPath)
    {
        folderPath = folderPath.Replace("\\", "/");
        if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath)) return;

        string[] folders = folderPath.Split('/');
        string currentPath = folders[0];

        for (int i = 1; i < folders.Length; i++)
        {
            if (string.IsNullOrEmpty(folders[i])) continue;
            string nextPath = currentPath + "/" + folders[i];
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, folders[i]);
            }
            currentPath = nextPath;
        }
    }

    private void ApplyAllRemaining()
    {
        EnsureFolderExists(AudioSupportTool.BGMFolder);
        EnsureFolderExists(AudioSupportTool.SEFolder);
        AssetDatabase.Refresh();

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = _currentIndex; i < _items.Count; i++)
            {
                _currentIndex = i;
                ApplyCurrent(false);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        _currentIndex = _items.Count;
        FinalizeImport();
    }

    private void FinalizeImport()
    {
        AssetDatabase.SaveAssets();
        if (AudioSupportTool.GenerateEnumEnabled)
        {
            AudioEnumGenerator.Generate();
        }
        Close();
    }

    private bool IsInvalidName(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        return !System.Text.RegularExpressions.Regex.IsMatch(text, @"^[a-zA-Z0-9_]+$");
    }
}
