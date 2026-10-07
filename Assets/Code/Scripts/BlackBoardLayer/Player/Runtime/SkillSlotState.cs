using System.Linq;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// スキルの装備枠ごとに、装備しているスキルの消費量を保持するステート。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class SkillSlotState : SceneStateBase, ISkillSlotState
    {
        private readonly int[] _costs;
        private readonly bool[] _equipped;

        public int SlotCount => _costs.Length;

        /// <param name="slotCount">装備枠の数。すべての枠は空きから始まる</param>
        public SkillSlotState(int slotCount)
        {
            _costs = new int[slotCount];
            _equipped = new bool[slotCount];
        }

        public override string GetLog()
        {
            return "Skill costs: " + string.Join(", ",
                Enumerable.Range(0, SlotCount).Select(i => _equipped[i] ? _costs[i].ToString() : "-"));
        }

        /// <summary>
        /// 枠にスキルを装備した状態にする。
        /// </summary>
        /// <param name="slot">枠の番号（0 から）</param>
        /// <param name="cost">装備したスキルの消費量</param>
        public void SetSlot(int slot, int cost)
        {
            _costs[slot] = cost;
            _equipped[slot] = true;
        }

        public bool TryGetCost(int slot, out int cost)
        {
            cost = _costs[slot];
            return _equipped[slot];
        }
    }

    /// <summary>
    /// スキルの装備枠の読み取り面。
    /// </summary>
    public interface ISkillSlotState : IStateGetter
    {
        /// <summary> 装備枠の数 </summary>
        int SlotCount { get; }

        /// <summary>
        /// 枠に装備しているスキルの消費量を取得する。
        /// </summary>
        /// <param name="slot">枠の番号（0 から）</param>
        /// <param name="cost">装備しているスキルの消費量</param>
        /// <returns>枠にスキルを装備していれば true、空きなら false</returns>
        bool TryGetCost(int slot, out int cost);
    }
}
