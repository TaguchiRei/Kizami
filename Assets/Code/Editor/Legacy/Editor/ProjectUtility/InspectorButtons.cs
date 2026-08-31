// [Legacy] 作り直しに伴い全体を無効化
#if false
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class InspectorButtons
{
    static InspectorButtons()
    {
        Editor.finishedDefaultHeaderGUI += OnPostHeaderGUI;
    }
    private static void OnPostHeaderGUI(Editor editor)
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        PropertiesWindowButton(editor);

        GUILayout.EndHorizontal();
    }

    private static void PropertiesWindowButton(Editor editor)
    {
        if (GUILayout.Button("Properties", GUILayout.Width(120)))
        {
            EditorUtility.OpenPropertyEditor(editor.target);
        }
    }
}
#endif
