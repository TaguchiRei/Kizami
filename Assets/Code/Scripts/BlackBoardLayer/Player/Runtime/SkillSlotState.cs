using System.Linq;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// スキルの装備枠ごとに、装備しているスキルの消費量とアイコンを保持するステート。
    /// </summary>
    [RegisterBoard(typeof(PlayerBoard))]
    public sealed class SkillSlotState : SceneStateBase, ISkillSlotState
    {
        private readonly int[] _costs;
        private readonly bool[] _equipped;
        private readonly Texture2D[] _icons;

        public int SlotCount => _costs.Length;

        /// <param name="slotCount">装備枠の数。すべての枠は空きから始まる</param>
        public SkillSlotState(int slotCount)
        {
            _costs = new int[slotCount];
            _equipped = new bool[slotCount];
            _icons = new Texture2D[slotCount];
        }

        public override string GetLog()
        {
            return "Skill costs: " + string.Join(", ",
                Enumerable.Range(0, SlotCount).Select(i => _equipped[i] ? _costs[i].ToString() : "-"));
        }

        /// <summary>
        /// 枠にスキルを装備した状態にする。
        /// </summary>
        /// <param name="slot">枠の番号（0から）</param>
        /// <param name="cost">装備したスキルの消費量</param>
        /// <param name="icon">装備したスキルのアイコン。ないときは null</param>
        public void SetSlot(int slot, int cost, Texture2D icon)
        {
            _costs[slot] = cost;
            _icons[slot] = icon;
            _equipped[slot] = true;
        }

        public bool TryGetCost(int slot, out int cost)
        {
            cost = _costs[slot];
            return _equipped[slot];
        }

        public Texture2D GetIcon(int slot)
        {
            return _icons[slot];
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
        /// <param name="slot">枠の番号（0から）</param>
        /// <param name="cost">装備しているスキルの消費量</param>
        /// <returns>枠にスキルを装備していれば true、空きなら false</returns>
        bool TryGetCost(int slot, out int cost);

        /// <summary>
        /// 枠に装備しているスキルのアイコンを取得する。
        /// </summary>
        /// <param name="slot">枠の番号（0から）</param>
        /// <returns>アイコン。空きの枠と、アイコンのないスキルは null</returns>
        Texture2D GetIcon(int slot);
    }
}
