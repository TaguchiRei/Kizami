using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.Application
{
    /// <summary>
    /// アウトゲームとインゲームの間で受け渡す進行（GameProgressState）を生成し、書き込むユースケース。
    /// ステージの番号とステージの定義（StageData）、スキルの番号とスキルの定義（SkillData）の対応を持つ。常駐シーンで一度だけ生成する。
    /// 初回クリアの通貨は、仕様でステージによらず同じ量なので、ステージの定義ではなくここで持つ。
    /// </summary>
    public sealed class GameProgressService
    {
        /// <summary> 初めから装備しているスキルの数。装備できるスキルの先頭から数える </summary>
        private const int INITIAL_EQUIPPED_COUNT = 2;

        private readonly GameProgressState _state = new(SkillService.SLOT_COUNT);

        private StageData[] _stages = Array.Empty<StageData>();
        private SkillData[] _skills = Array.Empty<SkillData>();
        private int _firstClearReward;

        /// <summary> 選べるステージ。番号はこの並びの位置 </summary>
        public IReadOnlyList<StageData> Stages => _stages;

        /// <summary> 装備できるスキル。番号はこの並びの位置 </summary>
        public IReadOnlyList<SkillData> Skills => _skills;

        /// <summary> 選んだステージの番号。選んでいなければ GameProgressState.NO_STAGE </summary>
        public int SelectedStageIndex => _state.SelectedStageIndex;

        // TODO: スキルの購入を作るときに、装備できるスキルを購入済みのものに絞る
        /// <summary>
        /// GameProgressState を AppBoard へ登録し、装備できるスキルの先頭から INITIAL_EQUIPPED_COUNT 個を、順に装備枠へ装備しておく。
        /// </summary>
        /// <param name="blackBoard">GameProgressState の登録先</param>
        /// <param name="stages">選べるステージ。番号はこの並びの位置</param>
        /// <param name="skills">装備できるスキル。番号はこの並びの位置</param>
        /// <param name="firstClearReward">ステージを初めてクリアしたときに得る通貨の量</param>
        public void Initialize(IBlackBoard blackBoard, StageData[] stages, SkillData[] skills, int firstClearReward)
        {
            _stages = stages ?? Array.Empty<StageData>();
            _skills = skills ?? Array.Empty<SkillData>();
            _firstClearReward = firstClearReward;

            for (var slot = 0; slot < Math.Min(Math.Min(_state.SlotCount, _skills.Length), INITIAL_EQUIPPED_COUNT); slot++)
            {
                _state.SetEquippedSkillIndex(slot, slot);
            }

            if (!blackBoard.TryGetBoard<AppBoard>(out var appBoard, this)) return;

            appBoard.RegisterGameState<IGameProgressState>(_state);
        }

        /// <summary>
        /// ステージを選ぶ。範囲外の番号は無視する。
        /// </summary>
        /// <param name="stageIndex">ステージの番号（0から）</param>
        /// <returns>選べたか</returns>
        public bool SelectStage(int stageIndex)
        {
            if (stageIndex < 0 || stageIndex >= _stages.Length)
            {
                UsefulLogger.LogError($"ステージの番号 {stageIndex} は範囲外です。", this);
                return false;
            }

            _state.SetSelectedStageIndex(stageIndex);
            return true;
        }

        /// <summary>
        /// 選んだステージの定義を取得する。
        /// </summary>
        /// <param name="stage">選んだステージの定義</param>
        /// <returns>ステージを選んでいるか</returns>
        public bool TryGetSelectedStage(out StageData stage)
        {
            var index = _state.SelectedStageIndex;
            stage = index >= 0 && index < _stages.Length ? _stages[index] : null;
            return stage != null;
        }

        /// <summary>
        /// 装備枠のスキルを、装備できるスキルの次のものに替える。最後のスキルの次は空きにし、空きの次は最初のスキルにする。
        /// </summary>
        /// <param name="slot">枠の番号（0から）</param>
        public void CycleEquippedSkill(int slot)
        {
            if (slot < 0 || slot >= _state.SlotCount) return;

            var next = _state.GetEquippedSkillIndex(slot) + 1;
            _state.SetEquippedSkillIndex(slot, next >= _skills.Length ? GameProgressState.NO_SKILL : next);
        }

        /// <summary>
        /// 装備枠ごとに、装備しているスキルの定義を返す。空きの枠は null。
        /// 選んだステージがスキルを固定しているときは、装備に関わらず、固定したスキルを先頭の枠から順に返す。
        /// </summary>
        public SkillData[] GetEquippedSkills()
        {
            var equipped = new SkillData[_state.SlotCount];
            var stageIndex = _state.SelectedStageIndex;
            if (stageIndex >= 0 && stageIndex < _stages.Length && _stages[stageIndex] != null &&
                _stages[stageIndex].FixedSkills.Count > 0)
            {
                var fixedSkills = _stages[stageIndex].FixedSkills;
                for (var slot = 0; slot < equipped.Length && slot < fixedSkills.Count; slot++)
                {
                    equipped[slot] = fixedSkills[slot];
                }

                return equipped;
            }

            for (var slot = 0; slot < equipped.Length; slot++)
            {
                var index = _state.GetEquippedSkillIndex(slot);
                equipped[slot] = index >= 0 && index < _skills.Length ? _skills[index] : null;
            }

            return equipped;
        }

        /// <summary>
        /// 選んだステージをクリアしたことにする。初めてのクリアなら通貨を足す。
        /// </summary>
        public void CompleteSelectedStage()
        {
            var index = _state.SelectedStageIndex;
            if (index < 0 || index >= _stages.Length || _state.IsStageCleared(index)) return;

            _state.MarkStageCleared(index);
            _state.SetCurrency(_state.Currency + _firstClearReward);
        }
    }
}
