using System;
using System.Collections.Generic;
using System.Linq;
using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.Application
{
    /// <summary>
    /// アウトゲームとインゲームの間で受け渡す進行（GameProgressState）を生成し、書き込むユースケース。
    /// ステージの番号とステージの定義（StageData）、スキルの番号とスキルの定義（SkillData）の対応を持つ。常駐シーンで一度だけ生成する。
    /// 進行はクリアと装備の変更のたびに保存し、生成するときに読み込む。
    /// </summary>
    public sealed class GameProgressService : IGameProgressController
    {
        private const string SAVE_FILE_NAME = "progress.json";

        private readonly GameProgressState _state = new(SkillService.SLOT_COUNT);
        private readonly JsonSaveStore<ProgressSaveData> _saveStore = new(SAVE_FILE_NAME);
        private readonly HashSet<string> _shownUnlockSkillIds = new();

        private StageData[] _stages = Array.Empty<StageData>();
        private SkillData[] _skills = Array.Empty<SkillData>();
        private int _ultimateSkillIndex = GameProgressState.NO_SKILL;

        /// <summary> 選べるステージ。番号はこの並びの位置 </summary>
        public IReadOnlyList<StageData> Stages => _stages;

        /// <summary> 装備できるスキル。番号はこの並びの位置 </summary>
        public IReadOnlyList<SkillData> Skills => _skills;

        /// <summary> スキル装備の画面で専用の枠に置くスキルの番号。なければ GameProgressState.NO_SKILL </summary>
        public int UltimateSkillIndex => _ultimateSkillIndex;

        /// <summary> 選んだステージの番号。選んでいなければ GameProgressState.NO_STAGE </summary>
        public int SelectedStageIndex => _state.SelectedStageIndex;

        /// <summary>
        /// GameProgressState を AppBoard へ登録し、保存した進行を読み込む。
        /// 保存データがなければ、解放済みのスキルを先頭から順に装備枠へ装備しておく。
        /// </summary>
        /// <param name="blackBoard">GameProgressState の登録先</param>
        /// <param name="stages">選べるステージ。番号はこの並びの位置で、この順に解放する</param>
        /// <param name="skills">装備できるスキル。番号はこの並びの位置</param>
        /// <param name="initialUnlockedSkills">初めから解放しておくスキル。skills に含まれるもの</param>
        /// <param name="ultimateSkill">専用の枠に置くスキル。skills に含まれるもの。なければ null</param>
        public void Initialize(IBlackBoard blackBoard, StageData[] stages, SkillData[] skills,
            SkillData[] initialUnlockedSkills, SkillData ultimateSkill)
        {
            _stages = stages ?? Array.Empty<StageData>();
            _skills = skills ?? Array.Empty<SkillData>();
            _ultimateSkillIndex = ultimateSkill != null ? Array.IndexOf(_skills, ultimateSkill) : GameProgressState.NO_SKILL;

            foreach (var skill in initialUnlockedSkills ?? Array.Empty<SkillData>())
            {
                UnlockSkill(skill);
            }

            if (_saveStore.TryLoad(out var saveData))
            {
                ApplySaveData(saveData);
            }
            else
            {
                EquipUnlockedSkillsInOrder();
            }

            if (!blackBoard.TryGetBoard<AppBoard>(out var appBoard, this)) return;

            appBoard.RegisterGameState<IGameProgressState>(_state);
        }

        /// <summary>
        /// ステージを選ぶ。範囲外の番号と、まだ選べないステージは無視する。
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

            if (!_state.IsStageUnlocked(stageIndex))
            {
                UsefulLogger.LogError($"ステージの番号 {stageIndex} は、前のステージをクリアしていない為選べません。", this);
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
        /// 装備枠にスキルを装備して保存する。解放していないスキルと範囲外の指定は無視する。
        /// ほかの枠に装備しているスキルを指定したときは、その枠と入れ替える。
        /// </summary>
        /// <param name="slot">枠の番号（0から）</param>
        /// <param name="skillIndex">スキルの番号（0から）。空きにするなら GameProgressState.NO_SKILL</param>
        /// <returns>装備を変えたか</returns>
        public bool EquipSkill(int slot, int skillIndex)
        {
            if (slot < 0 || slot >= _state.SlotCount) return false;
            if (skillIndex != GameProgressState.NO_SKILL &&
                (skillIndex < 0 || skillIndex >= _skills.Length || !_state.IsSkillUnlocked(skillIndex))) return false;

            var previous = _state.GetEquippedSkillIndex(slot);
            if (previous == skillIndex) return false;

            for (var other = 0; other < _state.SlotCount; other++)
            {
                if (other != slot && skillIndex != GameProgressState.NO_SKILL &&
                    _state.GetEquippedSkillIndex(other) == skillIndex)
                {
                    _state.SetEquippedSkillIndex(other, previous);
                }
            }

            _state.SetEquippedSkillIndex(slot, skillIndex);
            Save();
            return true;
        }

        /// <summary>
        /// ステージの初回クリアで解放されたスキルのうち、解放の演出をまだ出していないものを、ステージの並び順で演出の順番待ちに渡し、出したものとして記録して保存する。
        /// 記録は順番待ちに渡した時点で行う為、演出の途中で閉じても二度は出ない。
        /// </summary>
        /// <param name="queueNotice">演出を順番待ちに加える関数。引数はスキルの名前と、専用の枠のスキルか</param>
        public void QueuePendingUnlockNotices(Action<string, bool> queueNotice)
        {
            var ultimateSkill = UltimateSkillIndex >= 0 ? _skills[UltimateSkillIndex] : null;
            var isRecorded = false;
            for (var i = 0; i < _stages.Length; i++)
            {
                var skill = _stages[i] != null && _state.IsStageCleared(i) ? _stages[i].UnlockSkill : null;
                if (skill == null || Array.IndexOf(_skills, skill) < 0 || !_shownUnlockSkillIds.Add(skill.Id)) continue;

                queueNotice(skill.DisplayName, skill == ultimateSkill);
                isRecorded = true;
            }

            if (isRecorded) Save();
        }

        /// <summary>
        /// 装備枠ごとに、装備しているスキルの定義を返す。空きの枠は null。
        /// </summary>
        public SkillData[] GetEquippedSkills()
        {
            var equipped = new SkillData[_state.SlotCount];
            for (var slot = 0; slot < equipped.Length; slot++)
            {
                var index = _state.GetEquippedSkillIndex(slot);
                equipped[slot] = index >= 0 && index < _skills.Length ? _skills[index] : null;
            }

            return equipped;
        }

        /// <summary>
        /// 選んだステージをクリアしたことにする。初めてのクリアなら、ステージの定義が持つスキルを解放して保存する。
        /// </summary>
        public void CompleteSelectedStage()
        {
            var index = _state.SelectedStageIndex;
            if (index < 0 || index >= _stages.Length || _state.IsStageCleared(index)) return;

            MarkStageCleared(index);
            Save();
        }

        /// <summary>
        /// ステージをクリア済みにし、ステージの定義が持つスキルを解放する。
        /// </summary>
        /// <param name="stageIndex">ステージの番号（0から）</param>
        private void MarkStageCleared(int stageIndex)
        {
            _state.MarkStageCleared(stageIndex);
            if (_stages[stageIndex] != null) UnlockSkill(_stages[stageIndex].UnlockSkill);
        }

        /// <summary>
        /// スキルを解放済みにする。装備できるスキルに含まれないものは無視する。
        /// </summary>
        /// <param name="skill">解放するスキル</param>
        private void UnlockSkill(SkillData skill)
        {
            if (skill == null) return;

            var index = Array.IndexOf(_skills, skill);
            if (index >= 0) _state.UnlockSkill(index);
        }

        /// <summary>
        /// 装備枠へ装備できるか。解放済みで、ほかの枠に装備していないスキルだけを装備できる。
        /// </summary>
        /// <param name="slot">枠の番号（0から）</param>
        /// <param name="skillIndex">スキルの番号（0から）</param>
        private bool CanEquip(int slot, int skillIndex)
        {
            if (!_state.IsSkillUnlocked(skillIndex)) return false;

            for (var other = 0; other < _state.SlotCount; other++)
            {
                if (other != slot && _state.GetEquippedSkillIndex(other) == skillIndex) return false;
            }

            return true;
        }

        /// <summary>
        /// 解放済みのスキルを、先頭から順に装備枠へ装備する。
        /// </summary>
        private void EquipUnlockedSkillsInOrder()
        {
            var slot = 0;
            for (var index = 0; index < _skills.Length && slot < _state.SlotCount; index++)
            {
                if (!_state.IsSkillUnlocked(index)) continue;

                _state.SetEquippedSkillIndex(slot, index);
                slot++;
            }
        }

        /// <summary>
        /// 保存データを State へ流し込む。今の定義にない ID と、装備できないスキルは読み飛ばす。
        /// </summary>
        /// <param name="saveData">読み込んだ保存データ</param>
        private void ApplySaveData(ProgressSaveData saveData)
        {
            foreach (var stageId in saveData.ClearedStageIds ?? Array.Empty<string>())
            {
                var index = Array.FindIndex(_stages, stage => stage != null && stage.Id == stageId);
                if (index >= 0) MarkStageCleared(index);
            }

            foreach (var skillId in saveData.ShownUnlockSkillIds ?? Array.Empty<string>())
            {
                _shownUnlockSkillIds.Add(skillId);
            }

            var equippedIds = saveData.EquippedSkillIds ?? Array.Empty<string>();
            for (var slot = 0; slot < _state.SlotCount && slot < equippedIds.Length; slot++)
            {
                var index = Array.FindIndex(_skills, skill => skill != null && skill.Id == equippedIds[slot]);
                if (index >= 0 && CanEquip(slot, index)) _state.SetEquippedSkillIndex(slot, index);
            }
        }

        /// <summary>
        /// 今の進行を保存する。
        /// </summary>
        private void Save()
        {
            var equippedIds = new string[_state.SlotCount];
            for (var slot = 0; slot < equippedIds.Length; slot++)
            {
                var index = _state.GetEquippedSkillIndex(slot);
                equippedIds[slot] = index >= 0 && index < _skills.Length && _skills[index] != null
                    ? _skills[index].Id
                    : string.Empty;
            }

            _saveStore.Save(new ProgressSaveData
            {
                ClearedStageIds = Enumerable.Range(0, _stages.Length)
                    .Where(index => _state.IsStageCleared(index) && _stages[index] != null)
                    .Select(index => _stages[index].Id)
                    .ToArray(),
                EquippedSkillIds = equippedIds,
                ShownUnlockSkillIds = _shownUnlockSkillIds.ToArray(),
            });
        }
    }

    /// <summary>
    /// 進行を読み、装備とクリアを書き込む操作面。アウトゲームのメニューと、インゲームのステージ・プレイヤー・敵の初期化が使う。
    /// </summary>
    public interface IGameProgressController
    {
        /// <summary> 装備できるスキル。番号はこの並びの位置 </summary>
        IReadOnlyList<SkillData> Skills { get; }

        /// <summary> スキル装備の画面で専用の枠に置くスキルの番号。なければ GameProgressState.NO_SKILL </summary>
        int UltimateSkillIndex { get; }

        /// <summary>
        /// 選んだステージの定義を取得する。
        /// </summary>
        /// <param name="stage">選んだステージの定義</param>
        /// <returns>ステージを選んでいるか</returns>
        bool TryGetSelectedStage(out StageData stage);

        /// <summary>
        /// 装備枠にスキルを装備して保存する。
        /// </summary>
        /// <param name="slot">枠の番号（0から）</param>
        /// <param name="skillIndex">スキルの番号（0から）。空きにするなら GameProgressState.NO_SKILL</param>
        /// <returns>装備を変えたか</returns>
        bool EquipSkill(int slot, int skillIndex);

        /// <summary>
        /// 解放の演出をまだ出していないスキルを、演出の順番待ちに渡す。
        /// </summary>
        /// <param name="queueNotice">演出を順番待ちに加える関数。引数はスキルの名前と、専用の枠のスキルか</param>
        void QueuePendingUnlockNotices(Action<string, bool> queueNotice);

        /// <summary>
        /// 装備枠ごとに、装備しているスキルの定義を返す。空きの枠は null。
        /// </summary>
        SkillData[] GetEquippedSkills();

        /// <summary>
        /// 選んだステージをクリアしたことにする。
        /// </summary>
        void CompleteSelectedStage();
    }
}
