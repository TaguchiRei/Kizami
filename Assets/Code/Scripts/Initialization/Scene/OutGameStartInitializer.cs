using System.Collections.Generic;
using Kizami.Application;
using Kizami.BlackBoard;
using Kizami.EngineAdapter;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// アウトゲームの画面（OutGameMenuAdapter）を組み立て、ステージの開始、スキルの装備、コンフィグ、解放の演出をつなぐ Initializer。アウトゲームの場面シーンへ置く。
    /// インゲームから戻ってきたとき（選んだステージが残っているとき）は、ステージ選択から始める。
    /// </summary>
    public sealed class OutGameStartInitializer : InitializerBase, IInjectable<IGameSceneController>,
        IInjectable<GameProgressService>, IInjectable<IOperationSettingController>, IInjectable<InputRebindAdapter>
    {
        private IGameSceneController _sceneController;
        private GameProgressService _progressService;
        private IGameProgressState _progressState;
        private IOperationSettingController _settingController;
        private IOperationSettingState _settingState;
        private InputRebindAdapter _rebindAdapter;
        private OutGameMenuAdapter _menuAdapter;
        private bool _isTransitioning;

        /// <summary>
        /// 進行の State を取得し、画面を組み立てる。依存の注入より後に呼ばれる。
        /// </summary>
        public override void Initialize(IBlackBoard blackBoard)
        {
            blackBoard.TryGetGameState<AppBoard, IGameProgressState>(out _progressState, this);
            blackBoard.TryGetGameState<AppBoard, IOperationSettingState>(out _settingState, this);
            base.Initialize(blackBoard);

            if (_sceneController == null || _progressService == null || _progressState == null) return;

            _menuAdapter = gameObject.AddComponent<OutGameMenuAdapter>();
            _menuAdapter.SetConfig(_settingState, _settingController, _rebindAdapter);
            _menuAdapter.Initialize(BuildStageEntries(), BuildSkillEntries(), _progressService.UltimateSkillIndex,
                GetEquippedSkillIndices(), GoToStage, EquipSkill,
                _progressState.SelectedStageIndex != GameProgressState.NO_STAGE);
            _progressService.QueuePendingUnlockNotices(_menuAdapter.QueueUnlockNotice);
        }

        private List<OutGameMenuAdapter.StageEntry> BuildStageEntries()
        {
            var stages = _sceneController.Stages;
            var entries = new List<OutGameMenuAdapter.StageEntry>(stages.Count);
            for (var i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                if (stage == null)
                {
                    entries.Add(new OutGameMenuAdapter.StageEntry(string.Empty, string.Empty, false, false));
                    continue;
                }

                var unlockSkillName = stage.UnlockSkill != null ? stage.UnlockSkill.DisplayName : string.Empty;
                entries.Add(new OutGameMenuAdapter.StageEntry(stage.DisplayName, unlockSkillName,
                    _progressState.IsStageCleared(i), _progressState.IsStageUnlocked(i)));
            }

            return entries;
        }

        private List<OutGameMenuAdapter.SkillEntry> BuildSkillEntries()
        {
            var skills = _progressService.Skills;
            var entries = new List<OutGameMenuAdapter.SkillEntry>(skills.Count);
            for (var i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                entries.Add(skill == null
                    ? new OutGameMenuAdapter.SkillEntry(string.Empty, 0, string.Empty, false)
                    : new OutGameMenuAdapter.SkillEntry(skill.DisplayName, skill.Cost, skill.Description,
                        _progressState.IsSkillUnlocked(i)));
            }

            return entries;
        }

        private int[] GetEquippedSkillIndices()
        {
            var indices = new int[_progressState.SlotCount];
            for (var slot = 0; slot < indices.Length; slot++)
            {
                indices[slot] = _progressState.GetEquippedSkillIndex(slot);
            }

            return indices;
        }

        private void EquipSkill(int slot, int skillIndex)
        {
            if (_progressService.EquipSkill(slot, skillIndex)) _menuAdapter.SetEquipped(GetEquippedSkillIndices());
        }

        private async void GoToStage(int stageIndex)
        {
            if (_isTransitioning) return;

            _isTransitioning = true;
            _menuAdapter.SetInteractable(false);
            // このオブジェクトは遷移中のアンロードで破棄される。destroyCancellationToken を渡すと
            // アンロードの途中で中断され、SceneState に OutGame がロード済みのまま残る為、トークンは渡さない。
            await _sceneController.GoToStageAsync(stageIndex);
            _isTransitioning = false;
            if (_menuAdapter != null) _menuAdapter.SetInteractable(true);
        }

        public void Inject(IGameSceneController instance)
        {
            _sceneController = instance;
        }

        public void Inject(GameProgressService instance)
        {
            _progressService = instance;
        }

        public void Inject(IOperationSettingController instance)
        {
            _settingController = instance;
        }

        public void Inject(InputRebindAdapter instance)
        {
            _rebindAdapter = instance;
        }
    }
}
