using Kizami.BlackBoard;
using Kizami.EngineAdapter;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// 近接切断の実行役（MeleeCutAdapter）に PlayerEventBoard を渡して繋ぐだけの配線役。
    /// ロジックは持たない。MeshCut System と同じインゲームのシーンへ置く。
    /// </summary>
    public sealed class MeleeCutInitializer : InitializerBase
    {
        [SerializeField] private MeleeCutAdapter _adapter;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetEventBoard<PlayerEventBoard>(out var playerEventBoard))
            {
                UsefulLogger.LogError(
                    "PlayerEventBoard が未登録です。常駐シーンの Root Compositor を再生成してください。", this);
                base.Initialize(blackBoard);
                return;
            }

            if (_adapter != null)
            {
                _adapter.Initialize(playerEventBoard);
            }
            else
            {
                UsefulLogger.LogError("MeleeCutAdapter が設定されていません。", this);
            }

            base.Initialize(blackBoard);
        }
    }
}
