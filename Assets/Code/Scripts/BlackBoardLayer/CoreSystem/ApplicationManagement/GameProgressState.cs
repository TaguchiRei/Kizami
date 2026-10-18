using System.Collections.Generic;
using System.Linq;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// アウトゲームとインゲームの間で受け渡す進行（選んだステージ、装備したスキル、クリア済みのステージ）を保持するゲームステート。シーンを跨いで保たれる。
    /// ステージとスキルは番号で持ち、番号と定義の対応は Application が持つ。BlackBoard から External を参照できない為。
    /// </summary>
    [RegisterBoard(typeof(AppBoard))]
    public sealed class GameProgressState : GameStateBase, IGameProgressState
    {
        /// <summary> ステージを選んでいないことを表す番号 </summary>
        public const int NO_STAGE = -1;

        /// <summary> 装備枠が空きであることを表すスキルの番号 </summary>
        public const int NO_SKILL = -1;

        private readonly int[] _equippedSkillIndices;
        private readonly HashSet<int> _clearedStageIndices = new();

        public int SelectedStageIndex { get; private set; } = NO_STAGE;
        public int SlotCount => _equippedSkillIndices.Length;

        /// <param name="slotCount">スキルの装備枠の数。すべての枠は空きから始まる</param>
        public GameProgressState(int slotCount)
        {
            _equippedSkillIndices = Enumerable.Repeat(NO_SKILL, slotCount).ToArray();
        }

        public override string GetLog()
        {
            return $"SelectedStageIndex: {SelectedStageIndex}  \n" +
                   $"EquippedSkills: {string.Join(", ", _equippedSkillIndices)}  \n" +
                   $"ClearedStages: {string.Join(", ", _clearedStageIndices.OrderBy(i => i))}";
        }

        /// <summary>
        /// 選んだステージの番号を設定する。
        /// </summary>
        /// <param name="stageIndex">ステージの番号（0から）</param>
        public void SetSelectedStageIndex(int stageIndex)
        {
            SelectedStageIndex = stageIndex;
        }

        /// <summary>
        /// 装備枠に装備するスキルの番号を設定する。
        /// </summary>
        /// <param name="slot">枠の番号（0から）</param>
        /// <param name="skillIndex">スキルの番号。空きにするなら NO_SKILL</param>
        public void SetEquippedSkillIndex(int slot, int skillIndex)
        {
            _equippedSkillIndices[slot] = skillIndex;
        }

        /// <summary>
        /// ステージをクリア済みにする。
        /// </summary>
        /// <param name="stageIndex">ステージの番号（0から）</param>
        public void MarkStageCleared(int stageIndex)
        {
            _clearedStageIndices.Add(stageIndex);
        }

        public int GetEquippedSkillIndex(int slot)
        {
            return _equippedSkillIndices[slot];
        }

        public bool IsStageCleared(int stageIndex)
        {
            return _clearedStageIndices.Contains(stageIndex);
        }
    }

    /// <summary>
    /// アウトゲームとインゲームの間で受け渡す進行の読み取り面。
    /// </summary>
    public interface IGameProgressState : IStateGetter
    {
        /// <summary> 選んだステージの番号。選んでいなければ GameProgressState.NO_STAGE </summary>
        int SelectedStageIndex { get; }

        /// <summary> スキルの装備枠の数 </summary>
        int SlotCount { get; }

        /// <summary> 装備枠に装備しているスキルの番号。空きなら GameProgressState.NO_SKILL </summary>
        /// <param name="slot">枠の番号（0から）</param>
        int GetEquippedSkillIndex(int slot);

        /// <summary> ステージをクリアしたことがあるか </summary>
        /// <param name="stageIndex">ステージの番号（0から）</param>
        bool IsStageCleared(int stageIndex);
    }
}
