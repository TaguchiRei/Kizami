using Kizami.Application;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// 仮のアウトゲームに、スキルの装備枠、ステージのボタンを並べる Initializer。仮のアウトゲームの場面シーンへ置く。
    /// 装備枠のボタンを押すと装備できるスキルを順に替え、ステージのボタンを押すとそのステージのインゲームへ遷移する。まだ選べないステージのボタンは押せなくする。
    /// </summary>
    // TODO: アウトゲーム本来の流れ（ホーム、ステージ選択、装備の画面）に置き換える
    public sealed class OutGameStartInitializer : InitializerBase, IInjectable<IGameSceneController>,
        IInjectable<GameProgressService>
    {
        private const float AREA_WIDTH = 320f;
        private const float BUTTON_HEIGHT = 50f;
        private const string EMPTY_SLOT_TEXT = "(empty)";

        private IGameSceneController _sceneController;
        private GameProgressService _progressService;
        private IGameProgressState _progressState;
        private bool _isTransitioning;

        public override void Initialize(IBlackBoard blackBoard)
        {
            blackBoard.TryGetGameState<AppBoard, IGameProgressState>(out _progressState, this);
            base.Initialize(blackBoard);
        }

        private void OnGUI()
        {
            if (_sceneController == null || _isTransitioning) return;

            var area = new Rect(Screen.width * 0.5f - AREA_WIDTH * 0.5f, Screen.height * 0.2f, AREA_WIDTH,
                Screen.height * 0.6f);
            GUILayout.BeginArea(area);

            DrawProgress();
            DrawStages();

            GUILayout.EndArea();
        }

        /// <summary>
        /// スキルの装備枠のボタンを並べる。
        /// </summary>
        private void DrawProgress()
        {
            if (_progressService == null || _progressState == null) return;

            var skills = _progressService.Skills;
            for (var slot = 0; slot < _progressState.SlotCount; slot++)
            {
                var index = _progressState.GetEquippedSkillIndex(slot);
                var skillName = index >= 0 && index < skills.Count && skills[index] != null
                    ? skills[index].DisplayName
                    : EMPTY_SLOT_TEXT;

                if (GUILayout.Button($"Skill {slot + 1}: {skillName}", GUILayout.Height(BUTTON_HEIGHT)))
                {
                    _progressService.CycleEquippedSkill(slot);
                }
            }

            GUILayout.Space(BUTTON_HEIGHT * 0.5f);
        }

        /// <summary>
        /// ステージのボタンを並べる。クリアしたことのあるステージと、まだ選べないステージには印を付ける。
        /// </summary>
        private void DrawStages()
        {
            var stages = _sceneController.Stages;
            for (var i = 0; i < stages.Count; i++)
            {
                if (stages[i] == null) continue;

                var isCleared = _progressState != null && _progressState.IsStageCleared(i);
                var isUnlocked = _progressState == null || _progressState.IsStageUnlocked(i);
                var label = isCleared ? $"{stages[i].DisplayName} (cleared)"
                    : isUnlocked ? stages[i].DisplayName
                    : $"{stages[i].DisplayName} (locked)";

                GUI.enabled = isUnlocked;
                if (GUILayout.Button(label, GUILayout.Height(BUTTON_HEIGHT)))
                {
                    GoToStage(i);
                }

                GUI.enabled = true;
            }
        }

        private async void GoToStage(int stageIndex)
        {
            _isTransitioning = true;
            // このオブジェクトは遷移中のアンロードで破棄される。destroyCancellationToken を渡すと
            // アンロードの途中で中断され、SceneState に OutGame がロード済みのまま残る為、トークンは渡さない。
            await _sceneController.GoToStageAsync(stageIndex);
            _isTransitioning = false;
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
