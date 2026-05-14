#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace UsefulAttribute
{
    public class ShowOnlyAttribute : PropertyAttribute
    {
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ShowOnlyAttribute))]
    public class ShowOnlyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false; // 編集を無効化
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true; // 元に戻す
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
#endif
}