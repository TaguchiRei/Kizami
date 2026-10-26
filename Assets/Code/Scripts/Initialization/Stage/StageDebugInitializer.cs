using System.Text;
using Kizami.BlackBoard;
using Kizami.EngineAdapter;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Debugging;
using UsefulToolkit.Initialization;
using UsefulToolkit.Utility;

namespace Kizami.Initialization
{
    /// <summary>
    /// 流れの状態、破壊済みの破壊対象の数、破壊対象ごとの壊れ具合（ボクセルは重要パーツごとの削れた割合、コアは分離した部位の数）を画面に出すデバッグ用の Initializer。インゲームのシーンへ置く。
    /// 流れの State と破壊対象は StageInitializer が用意する為、それより後に初期化する。
    /// 表示には DebugGUI がシーンに必要（UsefulToolkit/ProgramTools/DebugGUI Setup）。エディタと Development Build でのみ動く。
    /// </summary>
    [InitializeOrder(InitializeOrderConst.DefaultLate)]
    public sealed class StageDebugInitializer : InitializerBase
    {
        private readonly StringBuilder _targetText = new();

        [SerializeField] private StageClearAdapter _stageClearAdapter;

        private IStageFlowState _flowState;

        public override void Initialize(IBlackBoard blackBoard)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this);
            DebugGUI.ObserveVariable("Stage", GetTargetText, this);
#endif
            base.Initialize(blackBoard);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 流れの状態と「破壊済みの数 / 破壊対象の数」のあとに、破壊対象ごとの壊れ具合を並べる。
        /// </summary>
        private string GetTargetText()
        {
            _targetText.Clear();
            if (_flowState != null)
            {
                _targetText.Append(_flowState.Phase).Append(' ')
                    .Append(_flowState.DestroyedTargetCount).Append(" / ").Append(_flowState.RequiredTargetCount)
                    .Append(" (of ").Append(_flowState.TargetCount).Append(')');
                if (_flowState.Phase == StagePhase.Failed) _targetText.Append(' ').Append(_flowState.FailReason);
            }

            if (_stageClearAdapter == null) return _targetText.ToString();

            foreach (var target in _stageClearAdapter.Targets)
            {
                if (target is Object unityObject && unityObject == null) continue;

                _targetText.Append(" | ");
                target.AppendStatus(_targetText);
            }

            return _targetText.ToString();
        }
#endif
    }
}
