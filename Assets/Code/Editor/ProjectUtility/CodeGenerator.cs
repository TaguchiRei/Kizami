using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class CodeGenerator : EditorWindow
{
    private string _codeName = "NewCode";
    private string _code;
    private Vector2 _scrollPosition;
    private GenerateMode _generateMode;

    private Func<string, string> _generateCodeFunc;

    //トグル表示
    private bool _showSimpleClass;
    private bool _showOthers;
    private bool _showOptions;

    private bool _isPushButton;
    private bool _isEmptyName;

    //コード改造
    private bool _isSerializable;
    private bool _useSummary;
    private AccessModifier _accessModifier;
    private OtherModifier _otherModifier;


    [MenuItem("UsefulTools/Code Generator")]
    public static void ShowWindow()
    {
        GetWindow<CodeGenerator>("Code Generator");
    }

    private void OnEnable()
    {
        _generateCodeFunc = GetSimpleCsCode;
        _generateMode = GenerateMode.SimpleCs;
        _showOptions = true;
        _showSimpleClass = true;
        _showOthers = true;
    }

    public void OnGUI()
    {
        GUILayout.Label("Name");
        EditorGUI.BeginChangeCheck();
        _codeName = EditorGUILayout.TextField(_codeName);
        if (EditorGUI.EndChangeCheck())
        {
            _isPushButton = true;
            _codeName = ToPascalCase(_codeName);
        }

        _showOptions = EditorGUILayout.Foldout(_showOptions, "Options");
        if (_showOptions)
        {
            EditorGUI.BeginChangeCheck();
            _useSummary = EditorGUILayout.Toggle("Use Summary", _useSummary);
            IsSerializable();
            _accessModifier = (AccessModifier)EditorGUILayout.EnumPopup("Access Modifier", _accessModifier);
            _otherModifier = (OtherModifier)EditorGUILayout.EnumPopup("Other Modifier", _otherModifier);

            if (EditorGUI.EndChangeCheck())
            {
                _isPushButton = true;
            }
        }


        GUILayout.Label("Code Preview");
        //コードプレビュー
        if (_isPushButton)
        {
            _code = _generateCodeFunc(_codeName);
            _isPushButton = false;
        }

        _code = EditorGUILayout.TextArea(_code, GUILayout.Height(position.height / 2f));

        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

        #region SimpleCodes

        _showSimpleClass = EditorGUILayout.Foldout(_showSimpleClass, new GUIContent("Simple Class"));
        if (_showSimpleClass)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("SimpleCS"))
            {
                _generateCodeFunc = GetSimpleCsCode;
                _generateMode = GenerateMode.SimpleCs;
                _isPushButton = true;
            }

            if (GUILayout.Button("MonoBehaviour"))
            {
                _generateCodeFunc = GetSimpleMonoBehaviourScript;
                _generateMode = GenerateMode.MonoBehaviour;
                _isPushButton = true;
            }

            if (GUILayout.Button("ScriptableObject"))
            {
                _generateCodeFunc = GetSimpleScriptableObjectScript;
                _generateMode = GenerateMode.ScriptableObject;
                _isPushButton = true;
            }

            if (GUILayout.Button("EditorWindow"))
            {
                _generateCodeFunc = GetSimpleEditorWindowScript;
                _generateMode = GenerateMode.EditorWindow;
                _isPushButton = true;
            }

            GUILayout.EndHorizontal();
        }

        #endregion

        _showOthers = EditorGUILayout.Foldout(_showOthers, new GUIContent("Others"));
        if (_showOthers)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Struct"))
            {
                _generateCodeFunc = GetStructCode;
                _generateMode = GenerateMode.Struct;
                _isPushButton = true;
            }

            if (GUILayout.Button("Enum"))
            {
                _generateCodeFunc = GetEnumCode;
                _generateMode = GenerateMode.Enum;
                _isPushButton = true;
            }

            if (GUILayout.Button("Interface"))
            {
                _generateCodeFunc = GetInterfaceCode;
                _generateMode = GenerateMode.Interface;
                _isPushButton = true;
            }

            GUILayout.EndHorizontal();
        }


        GUILayout.EndScrollView();

        if (string.IsNullOrEmpty(_codeName))
        {
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.red;
            style.fontStyle = FontStyle.Bold;
            GUILayout.Label("No Code name provided", style);
        }
        else if (GUILayout.Button("GenerateCode"))
        {
            GenerateCode(_code);
        }
    }

    #region Options

    private void IsSerializable()
    {
        if (_generateMode == GenerateMode.SimpleCs || _generateMode == GenerateMode.Struct)
        {
            _isSerializable = EditorGUILayout.Toggle("Is Serializable", _isSerializable);
        }
    }

    private string GetOtherModifier()
    {
        if (_otherModifier == OtherModifier.None)
        {
            return "";
        }
        else
        {
            return _otherModifier.ToString().ToLower();
        }
    }

    #endregion

    #region GenerateCode

    private void GenerateCode(string code)
    {
        string selectedPath =
            EditorUtility.OpenFolderPanel("Select Folder", ProjectWindowSelector.GetSelectedFolderPath(), _codeName);
        if (!string.IsNullOrEmpty(selectedPath))
        {
            if (selectedPath.StartsWith(Application.dataPath))
            {
                var folderPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                var p = GenerateCsFile(folderPath, code);
                ProjectWindowSelector.SelectAsset(p);
            }
            else
            {
                Debug.LogWarning("Assetsフォルダ内を選択してください");
            }
        }
    }

    /// <summary>
    /// CSファイルを生成する。
    /// </summary>
    /// <param name="path"></param>
    /// <param name="code"></param>
    private string GenerateCsFile(string path, string code)
    {
        var generatedPath = Path.Combine(path, _codeName + ".cs");
        File.WriteAllText(generatedPath, code);
        AssetDatabase.Refresh();

        Debug.Log("EditorWindowスクリプトを生成しました: " + path);
        return generatedPath;
    }

    #endregion

    #region GetCode

    private string GetSimpleCsCode(string className)
    {
        string code = _isSerializable ? "using System;\n[Serializable]\n" : "";
        code = _useSummary
            ? code + @"
/// <summary>
/// 
/// </summary>
"
            : code;

        code += $@"{_accessModifier.ToString().ToLower()} {GetOtherModifier()} class {className}
{{
    
}}";
        return code;
    }

    private string GetSimpleMonoBehaviourScript(string className)
    {
        string code = "using UnityEngine";

        code = _useSummary
            ? code + @"
/// <summary>
/// 
/// </summary>
"
            : code;

        code += $@";

{_accessModifier.ToString().ToLower()} {GetOtherModifier()}  class {className} : MonoBehaviour
{{
    
}}

";
        return code;
    }

    private string GetSimpleScriptableObjectScript(string className)
    {
        string code = "using UnityEngine;";
        code += _useSummary
            ? @"
/// <summary>
/// 
/// </summary>
"
            : "";

        code += $@"
[CreateAssetMenu(fileName = ""{className}"", menuName = ""ScriptableObjects/{className}"")]
{_accessModifier.ToString().ToLower()} {GetOtherModifier()}  class {className} : ScriptableObject
{{
    
}}
";
        return code;
    }

    private string GetSimpleEditorWindowScript(string className)
    {
        string code = "using UnityEngine;\nusing UnityEditor;";

        code += _useSummary
            ? @"
/// <summary>
/// 
/// </summary>
"
            : "";

        code += $@"
{_accessModifier.ToString().ToLower()} {GetOtherModifier()}  class {className} : EditorWindow
{{
    [MenuItem(""Window/UsefulTools/{className}"")]
    {_accessModifier.ToString().ToLower()} static void ShowWindow()
    {{
        GetWindow<{className}>(""{className}"");
    }}

    private void OnGUI()
    {{

    }}
}}";

        return code;
    }

    private string GetStructCode(string structName)
    {
        string code = _isSerializable ? "using System;\n[Serializable]\n" : "";
        code += $@"{_accessModifier.ToString().ToLower()}  struct {structName}
{{
    
}}";

        return code;
    }

    private string GetEnumCode(string enumName)
    {
        return $@"{_accessModifier.ToString().ToLower()} enum {enumName} 
{{
    
}}";
    }

    private string GetInterfaceCode(string interfaceName)
    {
        return $@"{_accessModifier.ToString().ToLower()}  interface {interfaceName}
{{
    
}}";
    }

    #endregion


    private string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        StringBuilder sb = new StringBuilder();

        sb.Append(char.ToUpper(input[0]));
        bool beforeSeparator = false;

        for (int i = 1; i < input.Length; i++)
        {
            var separator = IsSeparator(input[i]);
            if (separator)
            {
                beforeSeparator = true;
                continue;
            }

            sb.Append(beforeSeparator ? char.ToUpper(input[i]) : input[i]);

            beforeSeparator = false;
        }

        return sb.ToString();
    }

    private bool IsSeparator(char c)
    {
        return c == ' ' ||
               c == '_' ||
               c == '-' ||
               c == '/' ||
               c == '\n' ||
               c == '\r';
    }

    private enum GenerateMode
    {
        SimpleCs,
        MonoBehaviour,
        ScriptableObject,
        EditorWindow,
        Struct,
        Enum,
        Interface
    }

    private enum AccessModifier
    {
        Public,
        Protected,
        Internal,
        Private,
    }

    private enum OtherModifier
    {
        None,
        Abstract,
        Sealed,
        Static,
    }
}