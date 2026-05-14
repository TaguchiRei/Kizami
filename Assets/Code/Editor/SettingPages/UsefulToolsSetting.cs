using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UsefulTools.Editor
{
    public class UsefulToolsSetting : EditorWindow
    {
        private List<SettingPageBase> _pages;
        private string[] _tabNames;
        private int _selectedTabIndex = 0;
        private Vector2 _scrollPosition;

        private bool IsInitialized => _pages != null && _pages.Count > 0;

        [MenuItem("UsefulTools/Settings")]
        public static void ShowWindow()
        {
            var window = GetWindow<UsefulToolsSetting>("Useful Tools Settings");
            window.minSize = new Vector2(400, 300);
            window.EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// 初期化が確実に行われていることを保証する
        /// </summary>
        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                InitializePages();
            }
        }

        private void InitializePages()
        {
            // SettingPageBaseを継承している非抽象クラスをすべて取得
            var pageTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsSubclassOf(typeof(SettingPageBase)) && !t.IsAbstract);

            _pages = new List<SettingPageBase>();
            foreach (var type in pageTypes)
            {
                try
                {
                    var page = (SettingPageBase)Activator.CreateInstance(type);
                    page.Initialize();
                    _pages.Add(page);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UsefulTools] Failed to initialize setting page: {type.Name}\n{e}");
                }
            }

            // 名前の昇順でソート（Generalを先頭に、その他は名前順）
            _pages = _pages.OrderBy(p => p.Name == "General" ? "00_General" : p.Name).ToList();
            _tabNames = _pages.Select(p => p.Name).ToArray();
            
            if (_selectedTabIndex >= _pages.Count) _selectedTabIndex = 0;
        }

        private void OnGUI()
        {
            // GUI描画の直前で初期化を保証
            EnsureInitialized();

            DrawHeader();

            if (!IsInitialized)
            {
                EditorGUILayout.HelpBox("設定ページが見つかりません。SettingPageBaseを継承したクラスを作成してください。", MessageType.Warning);
                if (GUILayout.Button("初期化を再試行", GUILayout.Height(30))) InitializePages();
                return;
            }

            // タブの選択
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _selectedTabIndex = GUILayout.Toolbar(_selectedTabIndex, _tabNames, GUILayout.Height(25));
                if (check.changed)
                {
                    _scrollPosition = Vector2.zero; // ページ切り替え時にスクロールを戻す
                    GUI.FocusControl(null); // 前のページの入力フォーカスを外す
                }
            }

            EditorGUILayout.Space(10);

            // 選択されたページの描画
            using (var scroll = new EditorGUILayout.ScrollViewScope(_scrollPosition))
            {
                _scrollPosition = scroll.scrollPosition;
                
                using (new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins))
                {
                    _pages[_selectedTabIndex].OnGUI();
                }
            }
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Useful Tools Framework", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton))
                {
                    InitializePages();
                }
            }
            EditorGUILayout.Space(5);
        }
    }
}
