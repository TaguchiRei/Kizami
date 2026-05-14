using System;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UsefulAttribute
{
    [AttributeUsage(AttributeTargets.Method)]
    public class MethodExecutorAttribute : Attribute
    {
        public string ButtonName { get; }
        public bool CanExecuteInEditMode { get; }

        public MethodExecutorAttribute(string buttonName, bool canExecuteInEditMode)
        {
            ButtonName = buttonName;
            CanExecuteInEditMode = canExecuteInEditMode;
        }

        public MethodExecutorAttribute(bool canExecuteInEditMode)
        {
            ButtonName = "Test";
            CanExecuteInEditMode = canExecuteInEditMode;
        }

        public MethodExecutorAttribute(string buttonName)
        {
            ButtonName = buttonName;
            CanExecuteInEditMode = false;
        }

        public MethodExecutorAttribute()
        {
            ButtonName = "Test";
            CanExecuteInEditMode = false;
        }
    }

    // MonoBehaviour 用の基底クラス
    public class MethodExecutorBehaviour : MonoBehaviour
    {
    }

    // ScriptableObject 用の基底クラス
    public class MethodExecutorScriptableObject : ScriptableObject
    {
    }

#if UNITY_EDITOR
    /// <summary>
    /// MethodExecutorAttribute のボタン描画ロジックを共通化したクラス
    /// </summary>
    internal static class InspectorButtonDrawer
    {
        internal static void Draw(UnityEngine.Object target)
        {
            var methods = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<MethodExecutorAttribute>();
                if (attr == null) continue;

                if (method.GetParameters().Length > 0)
                {
                    EditorGUILayout.HelpBox(
                        $"{method.Name} はパラメータがあるため実行できません",
                        MessageType.Warning);
                    continue;
                }

                bool canExecute = Application.isPlaying || attr.CanExecuteInEditMode;
                GUI.enabled = canExecute;

                if (GUILayout.Button(attr.ButtonName))
                {
                    method.Invoke(target, null);
                }

                if (!canExecute)
                {
                    EditorGUILayout.HelpBox(
                        $"{method.Name} このメソッドはランタイム中のみ実行できます",
                        MessageType.Info);
                }

                GUI.enabled = true;
            }
        }
    }

    [CustomEditor(typeof(MethodExecutorBehaviour), true)]
    public class MethodExecutorBehaviourEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            InspectorButtonDrawer.Draw(target);
        }
    }

    [CustomEditor(typeof(MethodExecutorScriptableObject), true)]
    public class MethodExecutorScriptableObjectEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            InspectorButtonDrawer.Draw(target);
        }
    }
#endif
}