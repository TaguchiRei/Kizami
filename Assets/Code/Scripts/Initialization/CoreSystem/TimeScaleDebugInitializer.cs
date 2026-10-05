using Kizami.Application;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Debugging;
using UsefulToolkit.Initialization;
using UsefulToolkit.Utility;

namespace Kizami.Initialization
{
    /// <summary>
    /// 時間の倍率を実行中に変える操作と、倍率の画面表示を行うデバッグ用の Initializer。常駐シーンへ置く。
    /// 表示には DebugGUI がシーンに必要（UsefulToolkit/ProgramTools/DebugGUI Setup）。エディタと Development Build でのみ動く。
    /// </summary>
    [InitializeOrder(InitializeOrderConst.Initializer)]
    public sealed class TimeScaleDebugInitializer : InitializerBase, IInjectable<ITimeScaleController>
    {
        private static readonly float[] _presetScales = { 0f, 0.1f, 0.25f, 0.5f, 1f };

        private ITimeScaleController _controller;
        private ITimeScaleState _state;

        public void Inject(ITimeScaleController instance)
        {
            _controller = instance;
        }

        public override void Initialize(IBlackBoard blackBoard)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!blackBoard.TryGetGameState<AppBoard, ITimeScaleState>(out _state, this))
            {
                base.Initialize(blackBoard);
                return;
            }

            DebugGUI.ObserveVariable("TimeScale (State)", () => _state.Scale.ToString("0.00"));
            DebugGUI.ObserveVariable("Time.timeScale", () => Time.timeScale.ToString("0.00"));
            DebugGUI.ObserveVariable("Time.fixedDeltaTime", () => Time.fixedDeltaTime.ToString("0.0000"));
#endif
            base.Initialize(blackBoard);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (_controller == null || _state == null) return;

            const float WIDTH = 360f;
            GUILayout.BeginArea(new Rect(10f, Screen.height - 90f, WIDTH, 80f), GUI.skin.box);

            float scale = GUILayout.HorizontalSlider(_state.Scale, TimeScaleState.MIN_SCALE, TimeScaleState.MAX_SCALE);
            if (!Mathf.Approximately(scale, _state.Scale)) _controller.SetScale(scale);

            GUILayout.BeginHorizontal();
            foreach (float preset in _presetScales)
            {
                if (GUILayout.Button(preset.ToString("0.##"))) _controller.SetScale(preset);
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
#endif
    }
}
