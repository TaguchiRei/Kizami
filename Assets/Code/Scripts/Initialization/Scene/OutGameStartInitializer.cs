using System.Collections.Generic;
using Kizami.Application;
using Kizami.BlackBoard;
using Kizami.EngineAdapter;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// アウトゲームの画面（OutGameMenuAdapter）を組み立て、ステージの開始、スキルの装備枠の変更、ゲームの終了をつなぐ Initializer。アウトゲームの場面シーンへ置く。
    /// インゲームから戻ってきたとき（選んだステージが残っているとき）は、ステージ選択から始める。
    /// </summary>
    public sealed class OutGameStartInitializer : InitializerBase, IInjectable<IGameSceneController>,
        IInjectable<GameProgressService>
    {
        private const string EMPTY_SLOT_TEXT = "(empty)";

        private IGameSceneController _sceneController;
        private GameProgressService _progressService;
        private IGameProgressState _progressState;
        private OutGameMenuAdapter _menuAdapter;
        private bool _isTransitioning;

        /// <summary>
        /// 進行の State を取得し、画面を組み立てる。依存の注入より後に呼ばれる。
        /// </summary>
        public override void Initialize(IBlackBoard blackBoard)
        {
            blackBoard.TryGetGameState<AppBoard, IGameProgressState>(out _progressState, this);
            base.Initialize(blackBoard);

            if (_sceneController == null || _progressService == null || _progressState == null) return;

            _menuAdapter = gameObject.AddComponent<OutGameMenuAdapter>();
            _menuAdapter.Initialize(BuildStageEntries(), BuildSlotLabels(), GoToStage, CycleSlot, Quit,
                _progressState.SelectedStageIndex != GameProgressState.NO_STAGE);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
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

        private List<string> BuildSlotLabels()
        {
            var labels = new List<string>(_progressState.SlotCount);
            for (var slot = 0; slot < _progressState.SlotCount; slot++)
            {
                labels.Add(GetSlotSkillName(slot));
            }

            return labels;
        }

        private string GetSlotSkillName(int slot)
        {
            var skills = _progressService.Skills;
            var index = _progressState.GetEquippedSkillIndex(slot);
            return index >= 0 && index < skills.Count && skills[index] != null
                ? skills[index].DisplayName
                : EMPTY_SLOT_TEXT;
        }

        private void CycleSlot(int slot)
        {
            _progressService.CycleEquippedSkill(slot);
            _menuAdapter.SetSlotLabel(slot, GetSlotSkillName(slot));
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
    }
}
