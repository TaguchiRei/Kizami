using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class DebugGUI : MonoBehaviour
{
#if UNITY_EDITOR

    private Vector2 Position => new(
        UnityEditor.EditorPrefs.GetFloat("UsefulTools.Debug.PosX", 10f),
        UnityEditor.EditorPrefs.GetFloat("UsefulTools.Debug.PosY", 10f));

    private int FontSize => UnityEditor.EditorPrefs.GetInt("UsefulTools.Debug.FontSize", 20);
    private int FPSSampling => UnityEditor.EditorPrefs.GetInt("UsefulTools.Debug.FPSSampling", 10);
    private int MaxLogCount => UnityEditor.EditorPrefs.GetInt("UsefulTools.Debug.MaxLogCount", 10);
    private float LogTimeout => UnityEditor.EditorPrefs.GetFloat("UsefulTools.Debug.LogTimeout", 5.0f);

    private struct LogData
    {
        public string Message;
        public LogType Type;
        public float Time;
    }

    private struct ObserveData
    {
        public string Name;
        public Func<string> Getter;
    }

    private sealed class ObserveHandle : IDisposable
    {
        private DebugGUI _owner;
        private readonly int _id;

        public ObserveHandle(DebugGUI owner, int id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            if (_owner == null) return;

            _owner.RemoveObserve(_id);
            _owner = null;
        }
    }

#endif

    private static DebugGUI _instance;

    private GUIStyle _debugStyle;
    private GUIStyle _errorStyle;
    private GUIStyle _logStyle;

    private Rect _rect;

#if UNITY_EDITOR

    private readonly Dictionary<int, ObserveData> _observes = new(32);
    
    private readonly List<int> _observeKeys = new(32);

    private int _nextObserveId;

    private LogData[] _logs;
    private int _logStart;
    private int _logCount;

    private float[] _fpsSamples;
    private int _fpsIndex;
    private int _fpsCount;

    private readonly StringBuilder _stringBuilder = new(256);

    private readonly ConcurrentQueue<LogData> _threadedLogQueue = new();

#endif

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;

#if UNITY_EDITOR
            InitializeStyles();
            InitializeBuffers();

            Application.logMessageReceivedThreaded += OnLogReceived;
#endif

            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        Application.logMessageReceivedThreaded -= OnLogReceived;
#endif

        if (_instance == this)
        {
            _instance = null;
        }
    }

#if UNITY_EDITOR

    private void InitializeStyles()
    {
        _debugStyle = new GUIStyle
        {
            fontSize = FontSize,
            normal = { textColor = Color.white }
        };

        _errorStyle = new GUIStyle
        {
            fontSize = FontSize,
            normal = { textColor = Color.red }
        };

        _logStyle = new GUIStyle
        {
            fontSize = FontSize,
            alignment = TextAnchor.UpperRight,
            normal = { textColor = Color.white }
        };
    }

    private void InitializeBuffers()
    {
        _logs = new LogData[MaxLogCount];
        _fpsSamples = new float[FPSSampling];
    }

    private void OnLogReceived(string condition, string stackTrace, LogType type)
    {
        if (!UnityEditor.EditorPrefs.GetBool("UsefulTools.Debug.LogCaptureEnabled", false))
        {
            return;
        }

        _threadedLogQueue.Enqueue(new LogData
        {
            Message = condition,
            Type = type,
            Time = -1f
        });
    }

    private void Update()
    {
        ProcessThreadedLogs();
        UpdateFPS();
        UpdateLogs();
    }

    private void ProcessThreadedLogs()
    {
        while (_threadedLogQueue.TryDequeue(out var log))
        {
            AddLogInternal(log.Message, log.Type);
        }
    }

    private void UpdateFPS()
    {
        if (_fpsSamples == null || _fpsSamples.Length == 0)
        {
            return;
        }

        _fpsSamples[_fpsIndex] = Time.deltaTime;

        _fpsIndex++;

        if (_fpsIndex >= _fpsSamples.Length)
        {
            _fpsIndex = 0;
        }

        if (_fpsCount < _fpsSamples.Length)
        {
            _fpsCount++;
        }
    }

    private void UpdateLogs()
    {
        if (_logCount == 0)
        {
            return;
        }

        float current = Time.time;
        float timeout = LogTimeout;

        for (int i = 0; i < _logCount; i++)
        {
            int index = (_logStart + i) % _logs.Length;

            if (current - _logs[index].Time > timeout)
            {
                _logStart = (_logStart + 1) % _logs.Length;
                _logCount--;
                i--;
            }
        }
    }

    private void OnGUI()
    {
        if (_debugStyle == null || _debugStyle.fontSize != FontSize)
        {
            InitializeStyles();
        }

        if (_logs == null || _logs.Length != MaxLogCount)
        {
            InitializeBuffers();

            _logStart = 0;
            _logCount = 0;
        }

        if (!Mathf.Approximately(_rect.width, Screen.width) ||
            !Mathf.Approximately(_rect.height, Screen.height))
        {
            Vector2 pos = Position;

            _rect = new Rect(
                pos.x,
                pos.y,
                Screen.width,
                Screen.height);
        }

        bool prevEnabled = GUI.enabled;
        Color prevColor = GUI.color;

        GUI.enabled = false;
        GUI.color = Color.white;

        GUI.BeginGroup(_rect);

        GUILayout.BeginVertical();

        DrawFPS();
        DrawVariables();

        GUILayout.EndVertical();

        GUI.EndGroup();

        DrawLogs();

        GUI.enabled = prevEnabled;
        GUI.color = prevColor;
    }

    private void DrawFPS()
    {
        _stringBuilder.Clear();
        _stringBuilder.Append("FPS : ");
        _stringBuilder.Append((1f / Time.deltaTime).ToString("000.0"));

        GUILayout.Label(_stringBuilder.ToString(), _debugStyle);

        _stringBuilder.Clear();
        _stringBuilder.Append("Average FPS : ");
        _stringBuilder.Append(GetAverageFPS().ToString("000.0"));

        GUILayout.Label(_stringBuilder.ToString(), _debugStyle);
    }

    private void DrawVariables()
    {
        _observeKeys.Clear();

        foreach (var pair in _observes)
        {
            _observeKeys.Add(pair.Key);
        }

        int count = _observeKeys.Count;

        for (int i = 0; i < count; i++)
        {
            int key = _observeKeys[i];

            if (!_observes.TryGetValue(key, out var observe))
            {
                continue;
            }

            try
            {
                _stringBuilder.Clear();

                _stringBuilder.Append(observe.Name);
                _stringBuilder.Append(" : ");
                _stringBuilder.Append(observe.Getter());

                GUILayout.Label(_stringBuilder.ToString(), _debugStyle);
            }
            catch (MissingReferenceException)
            {
                _stringBuilder.Clear();

                _stringBuilder.Append(observe.Name);
                _stringBuilder.Append(" : Missing Reference");

                GUILayout.Label(_stringBuilder.ToString(), _errorStyle);

                _observes.Remove(key);
            }
            catch (NullReferenceException)
            {
                _stringBuilder.Clear();

                _stringBuilder.Append(observe.Name);
                _stringBuilder.Append(" : Null");

                GUILayout.Label(_stringBuilder.ToString(), _errorStyle);

                _observes.Remove(key);
            }
        }
    }

    private void DrawLogs()
    {
        float areaWidth = Screen.width * 0.5f;

        Rect logArea = new(
            Screen.width - areaWidth - 10,
            10,
            areaWidth,
            Screen.height - 20);

        GUILayout.BeginArea(logArea);

        GUILayout.BeginVertical();

        Color prevContentColor = GUI.contentColor;

        for (int i = 0; i < _logCount; i++)
        {
            int index =
                (_logStart + _logCount - 1 - i + _logs.Length) %
                _logs.Length;

            ref LogData log = ref _logs[index];

            GUI.contentColor = GetLogColor(log.Type);

            GUILayout.Label(log.Message, _logStyle);
        }

        GUI.contentColor = prevContentColor;

        GUILayout.EndVertical();

        GUILayout.EndArea();
    }

    private static Color GetLogColor(LogType type)
    {
        return type switch
        {
            LogType.Error or LogType.Exception or LogType.Assert => Color.red,
            LogType.Warning => Color.yellow,
            _ => Color.white
        };
    }

    private void AddObserve(int id, string name, Func<string> getter)
    {
        _observes.Add(id, new ObserveData
        {
            Name = name,
            Getter = getter
        });
    }

    private void RemoveObserve(int id)
    {
        _observes.Remove(id);
    }

#endif
    public static IDisposable ObserveVariable(
        string name,
        Func<string> getter)
    {
#if UNITY_EDITOR

        if (_instance == null)
        {
            Debug.LogWarning(
                "DebugGUIの初期化前にObserveVariableが呼ばれました");

            return null;
        }

        int id = _instance._nextObserveId++;

        _instance.AddObserve(id, name, getter);

        return new ObserveHandle(_instance, id);

#else
        return null;
#endif
    }

    [Conditional("UNITY_EDITOR")]
    public static void Log(string message)
    {
        AddLog(message, LogType.Log);
    }

    [Conditional("UNITY_EDITOR")]
    public static void LogWarning(string message)
    {
        AddLog(message, LogType.Warning);
    }

    [Conditional("UNITY_EDITOR")]
    public static void LogError(string message)
    {
        AddLog(message, LogType.Error);
    }

    private static void AddLog(string message, LogType type)
    {
#if UNITY_EDITOR

        switch (type)
        {
            case LogType.Log:
                Debug.Log(message);
                break;

            case LogType.Warning:
                Debug.LogWarning(message);
                break;

            case LogType.Error:
                Debug.LogError(message);
                break;
        }

#endif
    }

    private void AddLogInternal(string message, LogType type)
    {
#if UNITY_EDITOR

        if (_logs == null || _logs.Length == 0)
        {
            return;
        }

        int index = (_logStart + _logCount) % _logs.Length;

        _logs[index].Message = message;
        _logs[index].Type = type;
        _logs[index].Time = Time.time;

        if (_logCount < _logs.Length)
        {
            _logCount++;
        }
        else
        {
            _logStart = (_logStart + 1) % _logs.Length;
        }

#endif
    }

    private float GetAverageFPS()
    {
#if UNITY_EDITOR

        if (_fpsCount == 0)
        {
            return 0f;
        }

        float sum = 0f;

        for (int i = 0; i < _fpsCount; i++)
        {
            sum += _fpsSamples[i];
        }

        return 1f / (sum / _fpsCount);

#else
        return 0f;
#endif
    }
}