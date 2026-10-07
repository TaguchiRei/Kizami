using System.Text;
using Kizami.EngineAdapter;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Debugging;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// ステージのクリア判定（StageClearAdapter）を初期化し、破壊済みの破壊対象の数と、重要パーツごとの削れた割合を画面に出す配線役。インゲームのシーンへ置く。
    /// 表示には DebugGUI がシーンに必要（UsefulToolkit/ProgramTools/DebugGUI Setup）。表示はエディタと Development Build でのみ行う。
    /// </summary>
    public sealed class StageInitializer : InitializerBase
    {
        private readonly StringBuilder _targetText = new();

        [SerializeField] private StageClearAdapter _stageClearAdapter;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (_stageClearAdapter != null)
            {
                _stageClearAdapter.Initialize();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                DebugGUI.ObserveVariable("Destruction", GetTargetText);
#endif
            }
            else
            {
                UsefulLogger.LogError("StageClearAdapter が設定されていません。", this);
            }

            base.Initialize(blackBoard);
        }

        /// <summary>
        /// 「破壊済みの数 / 破壊対象の数」のあとに、破壊対象ごとの重要パーツの削れた割合と必要な割合を並べる。
        /// </summary>
        private string GetTargetText()
        {
            _targetText.Clear();
            _targetText.Append(_stageClearAdapter.DestroyedTargetCount).Append(" / ").Append(_stageClearAdapter.Targets.Count);
            if (_stageClearAdapter.IsCleared) _targetText.Append(" CLEAR");

            foreach (var target in _stageClearAdapter.Targets)
            {
                if (target == null) continue;

                _targetText.Append(" | ").Append(target.name).Append(':');
                for (var i = 0; i < target.ImportantPartCount; i++)
                {
                    _targetText.Append(' ');
                    _targetText.Append(target.TryGetDestroyedRatio(i, out var ratio) ? ratio.ToString("F2") : "-");
                }

                _targetText.Append(" / ").Append(target.RequiredRatio.ToString("F2"));
            }

            return _targetText.ToString();
        }
    }
}
