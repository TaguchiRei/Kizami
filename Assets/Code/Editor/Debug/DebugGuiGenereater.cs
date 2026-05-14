using UnityEditor;
using UnityEngine;


public class DebugGuiGenereater
{
    private bool _shoDebugGUI;
    private float _fontSize;
    private Vector2 _debugGUIPosition;
    
    [MenuItem("UsefulTools/Generate Debug GUI")]
    public static void GenerateDebugGUI()
    {
        var obj = new GameObject("DebugGUI");
        obj.AddComponent<DebugGUI>();
    }
}

