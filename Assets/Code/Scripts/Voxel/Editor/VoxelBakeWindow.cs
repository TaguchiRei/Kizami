using System;
using UnityEditor;
using UnityEngine;

namespace Kizami.Voxel.EditorTools
{
    /// <summary>
    /// モデルを選んで SDF にベイクし、VoxelModelAsset として保存するウィンドウ。
    /// 保存先は「出力フォルダ/モデル名_Voxel.asset」。
    /// </summary>
    public sealed class VoxelBakeWindow : EditorWindow
    {
        [SerializeField] private GameObject _source;
        [SerializeField] private VoxelBakeSettings _settings = VoxelBakeSettings.Default;
        [SerializeField] private string _outputFolder = "Assets/Data/Voxel/Baked";

        private SerializedObject _serializedObject;

        [MenuItem("Kizami/Voxel/Bake Model")]
        private static void Open()
        {
            GetWindow<VoxelBakeWindow>("Voxel Bake");
        }

        private void OnEnable()
        {
            _serializedObject = new SerializedObject(this);
        }

        private void OnGUI()
        {
            _serializedObject.Update();
            EditorGUILayout.PropertyField(_serializedObject.FindProperty(nameof(_source)), new GUIContent("モデル"));
            EditorGUILayout.PropertyField(_serializedObject.FindProperty(nameof(_settings)), new GUIContent("設定"), true);
            EditorGUILayout.PropertyField(_serializedObject.FindProperty(nameof(_outputFolder)), new GUIContent("出力フォルダ"));
            _serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_source == null))
            {
                if (GUILayout.Button("ベイク")) Bake();
            }
        }

        private void Bake()
        {
            var assetPath = $"{_outputFolder.TrimEnd('/')}/{_source.name}_Voxel.asset";

            try
            {
                var asset = VoxelModelBaker.BakeAndSave(_source, _settings, assetPath);

                var sampleTotal = 0;
                foreach (var part in asset.Parts)
                {
                    sampleTotal += part.Samples.Length;
                }

                Debug.Log($"ベイク完了: {assetPath}（パーツ {asset.Parts.Count} 個、サンプル {sampleTotal:N0} 個）", asset);
                EditorGUIUtility.PingObject(asset);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
