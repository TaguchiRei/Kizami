using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UsefulTools.Editor;

namespace UsefulTools.Editor
{
    [InitializeOnLoad]
    public class SceneEnumGenerator
    {
        /// <summary>
        /// Enum生成完了時に発行されるイベント
        /// </summary>
        public static event Action OnGenerated;

        static SceneEnumGenerator()
        {
            EditorBuildSettings.sceneListChanged += OnSceneListChanged;
            EditorSceneManager.newSceneCreated += OnNewSceneCreated;
        }

        private static void OnSceneListChanged()
        {
            if (SceneSupportTool.Timing != GenerateTiming.None)
            {
                Generate();
            }
        }

        private static void OnNewSceneCreated(UnityEngine.SceneManagement.Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
            if (SceneSupportTool.Timing != GenerateTiming.None)
            {
                Generate();
            }
        }

        [MenuItem("UsefulTools/Generate/Scene Enum")]
        public static void Generate()
        {
            string targetPath = SceneSupportTool.SceneSearchPath;
            // SceneSupportTool.EnumOutputPath からフォルダ部分を抽出
            string outputFolder = Path.GetDirectoryName(SceneSupportTool.EnumOutputPath).Replace("\\", "/");
            // 共通設定がある場合はそちらを優先する
            if (string.IsNullOrEmpty(outputFolder)) outputFolder = CodeSupportTool.EnumOutputFolder;
            
            string ns = SceneSupportTool.Namespace;
            if (string.IsNullOrEmpty(ns)) ns = CodeSupportTool.EnumNamespace;

            // ターゲットフォルダが存在するか確認
            if (!AssetDatabase.IsValidFolder(targetPath))
            {
                Debug.LogWarning($"[UsefulTools] Target scenes directory not found: {targetPath}");
                return;
            }

            // 指定ディレクトリ内の全シーンファイルを取得
            var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { targetPath });
            var scenePaths = sceneGuids.Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();

            // フィルタリング
            string[] ignorePatterns = SceneSupportTool.IgnorePatterns
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToArray();

            if (ignorePatterns.Length > 0)
            {
                scenePaths = scenePaths.Where(path => 
                    !ignorePatterns.Any(pattern => path.Contains(pattern))
                ).ToArray();
            }

            if (scenePaths.Length == 0)
            {
                Debug.Log($"[UsefulTools] No scenes found in {targetPath}");
                return;
            }

            // BuildSettings に登録されているかどうかの判定用
            var buildScenes = EditorBuildSettings.scenes.ToDictionary(s => s.path, s => s.enabled);

            var includedScenesList = new System.Collections.Generic.List<string>();
            var excludedScenesList = new System.Collections.Generic.List<string>();

            foreach (var path in scenePaths)
            {
                string sceneName = Path.GetFileNameWithoutExtension(path);
                string normalizedName = Regex.Replace(sceneName, @"[^a-zA-Z0-9_]", "_");

                if (buildScenes.TryGetValue(path, out bool enabled) && enabled)
                {
                    includedScenesList.Add(normalizedName);
                }
                else
                {
                    excludedScenesList.Add(normalizedName);
                }
            }

            // Enum生成実行
            EnumGenerator.GenerateEnum(SceneSupportTool.InListEnumName, includedScenesList.ToArray(), outputFolder, ns);
            EnumGenerator.GenerateEnum(SceneSupportTool.OutListEnumName, excludedScenesList.ToArray(), outputFolder, ns);
            
            Debug.Log($"[UsefulTools] SceneEnums generated to {outputFolder} with namespace {ns}");

            // イベント発行
            OnGenerated?.Invoke();
        }
    }
}
