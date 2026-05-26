using UnityEditor;
using UnityEngine;
using MeshBreak.MeshCut.Version2;
using System.Collections.Generic;

namespace MeshBreak.MeshCut.Editor
{
    public class MeshCutV2SetupEditor : EditorWindow
    {
        private GameObject _cuttablePrefab;
        private int _poolCapacity = 20;

        // Default settings for CuttableObject
        private Material _defaultCapMaterial;
        private PhysicsMaterial _defaultPhysicsMaterial;
        private int _defaultColliderNum = 10;

        // Collider Configs
        private float _baseShrink = 0.95f;
        private float _densityShrinkMin = 0.85f;
        private int _densityThreshold = 10;
        private float _maxRadius = 0.5f;

        private Vector2 _scrollPos;

        [MenuItem("Window/MeshCut/Version2 Setup")]
        public static void ShowWindow()
        {
            var window = GetWindow<MeshCutV2SetupEditor>("MeshCut V2 Setup");
            window.minSize = new Vector2(350, 650);
            window.titleContent = new GUIContent("MeshCut V2 Setup", EditorGUIUtility.IconContent("Settings").image);
        }

        private void OnEnable()
        {
            // Try to load default assets
            if (_defaultCapMaterial == null)
                _defaultCapMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/CapMat.mat");
            if (_defaultPhysicsMaterial == null)
                _defaultPhysicsMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/Code/Scripts/MeshBreak/MeshCut/Version2/MeshCutAssets/PhysixCollider/CutObjectMat.physicMaterial");
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();

            EditorGUILayout.Space(10);

            DrawSceneSetupSection();

            EditorGUILayout.Space(10);

            DrawSelectionToolSection();

            EditorGUILayout.Space(10);

            DrawStatusSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 40
            };
            headerStyle.normal.textColor = new Color(0.3f, 0.8f, 1f);

            EditorGUILayout.BeginVertical("helpBox");
            GUILayout.Label("MESH CUT V2 SETUP", headerStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawSceneSetupSection()
        {
            EditorGUILayout.BeginVertical("helpBox");
            GUILayout.Label(new GUIContent(" Scene Essentials", EditorGUIUtility.IconContent("SceneAsset Icon").image), EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            _cuttablePrefab = (GameObject)EditorGUILayout.ObjectField("Fragment Prefab", _cuttablePrefab, typeof(GameObject), false);
            _poolCapacity = EditorGUILayout.IntField("Pool Capacity", _poolCapacity);

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Setup Scene Essentials", GUILayout.Height(30)))
            {
                SetupScene();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectionToolSection()
        {
            EditorGUILayout.BeginVertical("helpBox");
            GUILayout.Label(new GUIContent(" Selection Tools", EditorGUIUtility.IconContent("FilterSelectedOnly").image), EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox("Select objects in Hierarchy to attach CuttableObject.\nRigidbody is only assigned if already present.", MessageType.Info);

            EditorGUILayout.Space(10);
            
            // Basic Settings
            GUILayout.Label("Basic Component Settings", EditorStyles.miniBoldLabel);
            _defaultCapMaterial = (Material)EditorGUILayout.ObjectField("Cap Material", _defaultCapMaterial, typeof(Material), false);
            _defaultPhysicsMaterial = (PhysicsMaterial)EditorGUILayout.ObjectField("Phys Material", _defaultPhysicsMaterial, typeof(PhysicsMaterial), false);
            _defaultColliderNum = EditorGUILayout.IntField("Collider Num", _defaultColliderNum);

            EditorGUILayout.Space(10);

            // Collider Settings
            GUILayout.Label("Advanced Collider Config", EditorStyles.miniBoldLabel);
            _baseShrink = EditorGUILayout.Slider("Base Shrink", _baseShrink, 0.5f, 1f);
            _densityShrinkMin = EditorGUILayout.Slider("Density Shrink Min", _densityShrinkMin, 0.5f, 1f);
            _densityThreshold = EditorGUILayout.IntSlider("Density Threshold", _densityThreshold, 1, 50);
            _maxRadius = EditorGUILayout.FloatField("Max Radius", _maxRadius);

            EditorGUILayout.Space(15);

            var selectedCount = Selection.gameObjects.Length;
            GUI.enabled = selectedCount > 0;
            
            var btnStyle = new GUIStyle(GUI.skin.button);
            btnStyle.fontStyle = FontStyle.Bold;
            btnStyle.fontSize = 13;
            if (EditorGUIUtility.isProSkin) btnStyle.normal.textColor = new Color(0.8f, 1f, 0.8f);

            if (GUILayout.Button($"Attach & Setup CuttableObject ({selectedCount})", btnStyle, GUILayout.Height(40)))
            {
                AddCuttableToSelected();
            }
            
            EditorGUILayout.Space(5);
            
            if (GUILayout.Button("Remove CuttableObject from Selected"))
            {
                RemoveCuttableFromSelected();
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndVertical();
        }

        private void DrawStatusSection()
        {
            EditorGUILayout.BeginVertical("helpBox");
            GUILayout.Label(new GUIContent(" Status", EditorGUIUtility.IconContent("console.infoicon").image), EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            DrawStatusRow("System Root", GameObject.Find("MeshCutSystem_V2") != null);
            DrawStatusRow("Blade", GameObject.Find("MeshCutBlade_V2") != null);
            DrawStatusRow("Object Pool", FindFirstObjectByType<MeshCutObjectPool>() != null);
            DrawStatusRow("Data Cache", FindFirstObjectByType<MeshDataCache>() != null);

            EditorGUILayout.EndVertical();
        }

        private void DrawStatusRow(string label, bool active)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(120));
            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = active ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);
            EditorGUILayout.LabelField(active ? "● Ready" : "○ Missing", style);
            EditorGUILayout.EndHorizontal();
        }

        private void SetupScene()
        {
            // 1. ルート管理オブジェクトの作成
            GameObject systemRoot = GameObject.Find("MeshCutSystem_V2");
            if (systemRoot == null)
            {
                systemRoot = new GameObject("MeshCutSystem_V2");
                Undo.RegisterCreatedObjectUndo(systemRoot, "Create System Root");
            }

            // 2. Cache オブジェクトの作成 (これから切るオブジェクトをこの下に置く)
            GameObject cacheObj = GameObject.Find("MeshCutCache_V2");
            if (cacheObj == null)
            {
                cacheObj = new GameObject("MeshCutCache_V2");
                cacheObj.transform.SetParent(systemRoot.transform);
                Undo.RegisterCreatedObjectUndo(cacheObj, "Create Cache Obj");
            }

            var cache = cacheObj.GetComponent<MeshDataCache>();
            if (cache == null)
            {
                cache = Undo.AddComponent<MeshDataCache>(cacheObj);
            }

            // 3. Pool オブジェクトの作成 (生成された破片がこの下に溜まる)
            GameObject poolObj = GameObject.Find("MeshCutPool_V2");
            if (poolObj == null)
            {
                poolObj = new GameObject("MeshCutPool_V2");
                poolObj.transform.SetParent(systemRoot.transform);
                Undo.RegisterCreatedObjectUndo(poolObj, "Create Pool Obj");
            }

            var pool = poolObj.GetComponent<MeshCutObjectPool>();
            if (pool == null)
            {
                pool = Undo.AddComponent<MeshCutObjectPool>(poolObj);
            }

            // Pool の設定
            var poolSerialized = new SerializedObject(pool);
            poolSerialized.FindProperty("_generateCapacity").intValue = _poolCapacity;
            if (_cuttablePrefab != null)
            {
                poolSerialized.FindProperty("_prefab").objectReferenceValue = _cuttablePrefab;
            }
            poolSerialized.ApplyModifiedProperties();

            // 4. Blade の作成
            GameObject blade = GameObject.Find("MeshCutBlade_V2");
            if (blade == null)
            {
                blade = new GameObject("MeshCutBlade_V2");
                blade.transform.position = new Vector3(0, 2, 0);
                Undo.RegisterCreatedObjectUndo(blade, "Create Blade");
            }

            var box = blade.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = Undo.AddComponent<BoxCollider>(blade);
                box.isTrigger = true;
                box.size = new Vector3(5, 0.1f, 5);
            }

            var multiBlade = blade.GetComponent<MultiCutBlade>();
            if (multiBlade == null)
            {
                multiBlade = Undo.AddComponent<MultiCutBlade>(blade);
            }

            // Blade に Pool を紐付け
            var bladeSerialized = new SerializedObject(multiBlade);
            bladeSerialized.FindProperty("_pool").objectReferenceValue = pool;
            bladeSerialized.ApplyModifiedProperties();

            Selection.activeGameObject = systemRoot;
            Debug.Log("[MeshCut V2] Scene setup completed.");
        }

        private void AddCuttableToSelected()
        {
            var selected = Selection.gameObjects;
            int count = 0;

            foreach (var obj in selected)
            {
                var mf = obj.GetComponent<MeshFilter>();
                var renderer = obj.GetComponent<Renderer>();

                if (mf == null || renderer == null)
                {
                    Debug.LogWarning($"[MeshCut V2] {obj.name} は MeshFilter または Renderer が不足しているためスキップされました。");
                    continue;
                }

                // CuttableObject の追加（無ければ）
                var cuttable = obj.GetComponent<CuttableObject>();
                if (cuttable == null)
                {
                    cuttable = Undo.AddComponent<CuttableObject>(obj);
                }

                // ★ SerializedObject はコンポーネント取得/追加の後に作成
                var so = new SerializedObject(cuttable);
                so.Update(); // ★ 必ず Update() を呼ぶ

                // Public フィールドも SerializedObject 経由で統一
                so.FindProperty("Renderer").objectReferenceValue = renderer;
                so.FindProperty("Mesh").objectReferenceValue = mf;
                so.FindProperty("Rig").objectReferenceValue = obj.GetComponent<Rigidbody>();

                if (_defaultCapMaterial != null)
                    so.FindProperty("CapMaterial").objectReferenceValue = _defaultCapMaterial;

                // Private [SerializeField] フィールド
                so.FindProperty("_physicsMaterial").objectReferenceValue = _defaultPhysicsMaterial;
                so.FindProperty("_colliderNum").intValue = _defaultColliderNum;
                so.FindProperty("_baseShrink").floatValue = _baseShrink;
                so.FindProperty("_densityShrinkMin").floatValue = _densityShrinkMin;
                so.FindProperty("_densityThreshold").intValue = _densityThreshold;
                so.FindProperty("_maxRadius").floatValue = _maxRadius;

                // ★ ApplyModifiedProperties() で確定させる
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(obj); // ★ コンポーネントではなくGameObject側もDirtyに

                count++;
            }

            Debug.Log($"[MeshCut V2] {count} 個のオブジェクトに CuttableObject をセットアップしました。");
        }

        private void RemoveCuttableFromSelected()
        {
            int count = 0;
            foreach (var obj in Selection.gameObjects)
            {
                var cuttable = obj.GetComponent<CuttableObject>();
                if (cuttable != null)
                {
                    Undo.DestroyObjectImmediate(cuttable);
                    count++;
                }
            }
            Debug.Log($"[MeshCut V2] {count} 個のオブジェクトから CuttableObject を削除しました。");
        }
    }
}
