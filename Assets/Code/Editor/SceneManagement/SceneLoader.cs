using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UsefulTools.Editor
{
    public class SceneLoader : EditorWindow
    {
        private List<SceneInfo> _onListScenes = new List<SceneInfo>();
        private List<SceneInfo> _outListScenes = new List<SceneInfo>();
        private Vector2 _onListScroll;
        private Vector2 _outListScroll;
        private string _searchText = "";
        private bool _groupByFolder = false;

        private struct SceneInfo
        {
            public string Name;
            public string Path;
            public string Folder;
        }

        [MenuItem("UsefulTools/Scene Loader")]
        public static void ShowWindow()
        {
            var window = GetWindow<SceneLoader>("Scene Loader");
            window.Initialize();
        }

        private void OnEnable()
        {
            SceneEnumGenerator.OnGenerated += OnEnumGenerated;
            Initialize();
        }

        private void OnDisable()
        {
            SceneEnumGenerator.OnGenerated -= OnEnumGenerated;
        }

        private void OnFocus()
        {
            Initialize();
        }

        private void OnEnumGenerated()
        {
            if (SceneSupportTool.Timing == GenerateTiming.OnToolUpdate)
            {
                Initialize();
                Repaint();
            }
        }

        private void Initialize()
        {
            string targetPath = SceneSupportTool.SceneSearchPath;
            if (!AssetDatabase.IsValidFolder(targetPath)) return;

            var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { targetPath });
            var scenePaths = sceneGuids.Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();

            // フィルタリング（Generatorと同じルールを適用）
            string[] ignorePatterns = SceneSupportTool.IgnorePatterns
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToArray();

            var buildScenes = EditorBuildSettings.scenes.ToDictionary(s => s.path, s => s.enabled);

            _onListScenes.Clear();
            _outListScenes.Clear();

            foreach (var path in scenePaths)
            {
                if (ignorePatterns.Any(pattern => path.Contains(pattern))) continue;

                var info = new SceneInfo
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    Folder = Path.GetDirectoryName(path).Replace("\\", "/").Replace(targetPath, "")
                };

                if (buildScenes.TryGetValue(path, out bool enabled) && enabled)
                {
                    _onListScenes.Add(info);
                }
                else
                {
                    _outListScenes.Add(info);
                }
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            using (new EditorGUILayout.HorizontalScope())
            {
                // On List Scenes
                DrawSceneList("Build Settings (Included)", _onListScenes, ref _onListScroll);
                // Out List Scenes
                DrawSceneList("Project (Excluded)", _outListScenes, ref _outListScroll);
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) Initialize();
                
                GUILayout.Space(5);
                _groupByFolder = GUILayout.Toggle(_groupByFolder, "Group by Folder", EditorStyles.toolbarButton);
                
                GUILayout.FlexibleSpace();
                
                GUILayout.Label("Search:", EditorStyles.miniLabel);
                _searchText = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField, GUILayout.Width(150));
                if (!string.IsNullOrEmpty(_searchText))
                {
                    if (GUILayout.Button("", "ToolbarSearchCancelButton"))
                    {
                        _searchText = "";
                        GUI.FocusControl(null);
                    }
                }
                else
                {
                    GUILayout.Box("", "ToolbarSearchCancelButtonEmpty", GUILayout.Width(16));
                }
            }
        }

        private void DrawSceneList(string title, List<SceneInfo> scenes, ref Vector2 scrollPos)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                
                using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPos, EditorStyles.helpBox))
                {
                    scrollPos = scroll.scrollPosition;

                    var filteredScenes = scenes;
                    if (!string.IsNullOrEmpty(_searchText))
                    {
                        filteredScenes = scenes.Where(s => s.Name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    }

                    if (_groupByFolder)
                    {
                        var groups = filteredScenes.GroupBy(s => s.Folder).OrderBy(g => g.Key);
                        foreach (var group in groups)
                        {
                            string folderName = string.IsNullOrEmpty(group.Key) ? "/" : group.Key;
                            EditorGUILayout.LabelField(folderName, EditorStyles.miniBoldLabel);
                            foreach (var scene in group) DrawSceneButton(scene);
                            EditorGUILayout.Space(2);
                        }
                    }
                    else
                    {
                        foreach (var scene in filteredScenes) DrawSceneButton(scene);
                    }
                }
            }
        }

        private void DrawSceneButton(SceneInfo scene)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(scene.Name, GUILayout.Height(22)))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        EditorSceneManager.OpenScene(scene.Path);
                    }
                }
                
                if (GUILayout.Button("P", GUILayout.Width(20), GUILayout.Height(22)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.Path);
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }
    }
}
