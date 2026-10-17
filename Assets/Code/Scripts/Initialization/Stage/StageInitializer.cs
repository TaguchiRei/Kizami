using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Kizami.Application;
using Kizami.BlackBoard;
using Kizami.EngineAdapter;
using Kizami.EngineAdapter.Voxel;
using Kizami.External;
using UnityEngine;
using UnityEngine.SceneManagement;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Debugging;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// インゲームの流れ（StageFlowService / StageFlowState）と、ステージの案内（StageGuideService / StageGuideState）、破壊対象・制限時間・HUD・案内・ポーズ・リザルトの Adapter、ステージシーンにある台本（StageGuideScript）と開始位置の目印（PlayerStartPoint）を繋いで、プレイを始める配線役。インゲームのシーンへ置く。
    /// Brilliant Skill の判定（BrilliantSkillService）に、敵を倒した通知、ボクセルを壊した体積、建物の倒壊を繋ぎ、成立したら追加スコアを足す。
    /// 失敗条件の判定と、案内のログにプレイヤーの State と出来事の通知を使う為、PlayerInitializer より後に初期化する。
    /// 流れの状態、破壊済みの破壊対象の数、破壊対象ごとの壊れ具合（ボクセルは重要パーツごとの削れた割合、コアは分離した部位の数）を画面に出す。表示には DebugGUI がシーンに必要（UsefulToolkit/ProgramTools/DebugGUI Setup）。表示はエディタと Development Build でのみ行う。
    /// </summary>
    public sealed class StageInitializer : InitializerBase, IInjectable<GameProgressService>, IInjectable<IGameSceneController>,
        IInjectable<ITimeScaleController>, IInjectable<IInputController>
    {
        private readonly StringBuilder _targetText = new();
        private readonly List<System.IDisposable> _buildingSubscriptions = new();

        [SerializeField] private StageClearAdapter _stageClearAdapter;
        [SerializeField] private EnemySpawnAdapter _enemySpawnAdapter;
        [SerializeField] private StageResultAdapter _resultAdapter;
        [SerializeField] private StageHudAdapter _hudAdapter;
        [SerializeField] private StageGuideAdapter _guideAdapter;
        [SerializeField] private StagePauseAdapter _pauseAdapter;
        [SerializeField] private FragmentThrowAdapter _fragmentThrowAdapter;
        [SerializeField] private StageTimerAdapter _timerAdapter;

        [SerializeField, Min(1)]
        [Tooltip("案内のログを持つ件数の上限。超えたら古いものから消える")]
        private int _guideLogMaxCount = 8;

        [SerializeField, Min(1f)]
        [Tooltip("案内のログを出しておく時間（秒）")]
        private float _guideLogLifetime = 5f;

        [SerializeField]
        [Tooltip("Brilliant Skill の判定の条件と追加スコア")]
        private BrilliantSkillSettings _brilliantSkillSettings = new();

        private GameProgressService _progressService;
        private IGameSceneController _sceneController;
        private ITimeScaleController _timeScaleController;
        private IInputController _inputController;
        private StageFlowService _flowService;
        private StageGuideService _guideService;
        private BrilliantSkillService _brilliantSkillService;
        private VoxelDestructionAdapter _voxelDestructionAdapter;

        /// <summary> 投げた言葉の着弾を処理している間は true。着弾で倒した敵と壊したボクセルは、Brilliant Skill の判定に伝えない </summary>
        private bool _isResolvingWordImpact;
        private IStageFlowState _flowState;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (!HasRequiredReferences())
            {
                base.Initialize(blackBoard);
                return;
            }

            if (_timeScaleController == null || _inputController == null)
            {
                UsefulLogger.LogError("ITimeScaleController か IInputController を受け取れなかった為、ポーズを使えません。", this);
            }

            var stage = GetSelectedStage();
            _flowService = new StageFlowService(blackBoard, stage, _timeScaleController, _inputController,
                _progressService != null ? _progressService.CompleteSelectedStage : null, gameObject.scene.buildIndex);
            blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this);
            _stageClearAdapter.Initialize(_flowService.ReportTargets, stage.TargetGimmickIds);
            _timerAdapter.Initialize(_flowService.Tick);
            _guideService = new StageGuideService(blackBoard, _guideLogMaxCount, _guideLogLifetime,
                gameObject.scene.buildIndex);
            var textAdapter = FindOrCreateBrilliantSkillTextAdapter();
            _fragmentThrowAdapter.SetWordSource(textAdapter);
            textAdapter.SetOnWordLanded(OnWordLanded);
            ConnectWordCutting(textAdapter);
            _brilliantSkillService = new BrilliantSkillService(_brilliantSkillSettings, _flowService.AddBonusScore,
                kind => textAdapter.Show(_brilliantSkillSettings.GetWord(kind)));
            _enemySpawnAdapter.SetOnDefeated(OnDefeated);
            ConnectBrilliantSkillSources();
            PlacePlayerAtStartPoint();

            // 台本はステージシーンにあり、置いていないステージでは案内の文とログだけを出す
            var guideScript = FindFirstObjectByType<StageGuideScript>();
            if (guideScript != null)
            {
                guideScript.Initialize(blackBoard, _guideService.SetObjective, _guideService.SetSubObjective,
                    _guideService.SetDialogue, _guideService.SetHint, _flowService.Clear);
            }

            _resultAdapter.Initialize(blackBoard, Retry, BackToOutGame);
            _hudAdapter.Initialize(blackBoard, _fragmentThrowAdapter);
            _guideAdapter.Initialize(blackBoard);
            _pauseAdapter.Initialize(blackBoard, _flowService.Resume, BackToOutGame);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugGUI.ObserveVariable("Stage", GetTargetText, this);
#endif

            base.Initialize(blackBoard);
            _flowService.Begin();
        }

        /// <summary>
        /// ステージシーンに開始位置の目印があれば、プレイヤーをその位置へ置く。置いていないステージでは動かさない。
        /// </summary>
        private void PlacePlayerAtStartPoint()
        {
            var startPoint = FindFirstObjectByType<PlayerStartPoint>();
            if (startPoint == null) return;

            var movementAdapter = FindFirstObjectByType<PlayerMovementAdapterBase>();
            if (movementAdapter == null)
            {
                UsefulLogger.LogError("PlayerMovementAdapterBase が見つからない為、開始位置へ置けません。", this);
                return;
            }

            movementAdapter.PlaceAt(startPoint.transform.position);
        }

        /// <summary>
        /// 必須の参照がすべて設定されているかを返す。足りないものは、すべてエラーログに出す。
        /// </summary>
        private bool HasRequiredReferences()
        {
            var hasAll = this.IsAssigned(_stageClearAdapter, nameof(StageClearAdapter));
            hasAll &= this.IsAssigned(_enemySpawnAdapter, nameof(EnemySpawnAdapter));
            hasAll &= this.IsAssigned(_resultAdapter, nameof(StageResultAdapter));
            hasAll &= this.IsAssigned(_hudAdapter, nameof(StageHudAdapter));
            hasAll &= this.IsAssigned(_guideAdapter, nameof(StageGuideAdapter));
            hasAll &= this.IsAssigned(_pauseAdapter, nameof(StagePauseAdapter));
            hasAll &= this.IsAssigned(_fragmentThrowAdapter, nameof(FragmentThrowAdapter));
            hasAll &= this.IsAssigned(_timerAdapter, nameof(StageTimerAdapter));
            return hasAll;
        }

        /// <summary>
        /// 倒した敵の数を、流れの State と案内のログへ渡し、Brilliant Skill の判定に伝える。
        /// </summary>
        private void OnDefeated(EnemyDefeatNotice notice)
        {
            _flowService.AddDefeats(notice.Count);
            _guideService.AddDefeats(notice.Count);
            if (!_isResolvingWordImpact) _brilliantSkillService.OnDefeated(notice);
        }

        /// <summary>
        /// 投げた言葉がぶつかった点で、ボクセルを削り、範囲の中の敵を倒す。
        /// 着弾の結果で別の言葉が出て、それをまた投げて追加スコアを重ねられないよう、この間の撃破と破壊は Brilliant Skill の判定に伝えない。
        /// </summary>
        private void OnWordLanded(Vector3 point)
        {
            var radius = _brilliantSkillSettings.WordImpactRadius;
            _isResolvingWordImpact = true;
            try
            {
                if (_voxelDestructionAdapter != null) _voxelDestructionAdapter.CarveExplosionAt(point, radius);
                _enemySpawnAdapter.DefeatInRadius(point, radius);
            }
            finally
            {
                _isResolvingWordImpact = false;
            }
        }

        /// <summary>
        /// ボクセルを壊した体積を Brilliant Skill の判定に伝える。投げた言葉の着弾による破壊は伝えない。
        /// </summary>
        private void OnVoxelsCarved(float volume)
        {
            if (!_isResolvingWordImpact) _brilliantSkillService.OnVoxelsDestroyed(volume);
        }

        /// <summary>
        /// ステージシーンに置かれた Brilliant Skill の言葉の Adapter を返す。置かれていなければ、既定の設定で作ってステージシーンに入れる（ステージを読み直すとシーンごと消える）。
        /// </summary>
        private BrilliantSkillTextAdapter FindOrCreateBrilliantSkillTextAdapter()
        {
            var adapter = FindFirstObjectByType<BrilliantSkillTextAdapter>();
            if (adapter != null) return adapter;

            var adapterObject = new GameObject(nameof(BrilliantSkillTextAdapter));
            SceneManager.MoveGameObjectToScene(adapterObject, gameObject.scene);
            return adapterObject.AddComponent<BrilliantSkillTextAdapter>();
        }

        /// <summary>
        /// 通常攻撃の切断で文字を切れるようにする。ステージシーンに切断の Adapter かオーブの Adapter がなければ、文字は切れない。
        /// </summary>
        private void ConnectWordCutting(BrilliantSkillTextAdapter textAdapter)
        {
            var cutter = FindFirstObjectByType<MeleeCutAdapter>();
            var orbAdapter = FindFirstObjectByType<FragmentOrbAdapter>();
            if (cutter == null || orbAdapter == null)
            {
                UsefulLogger.LogWarning("MeleeCutAdapter か FragmentOrbAdapter が見つからない為、文字を切れません。", this);
                return;
            }

            textAdapter.SetCutter(cutter, orbAdapter.TryTake);
        }

        /// <summary>
        /// Brilliant Skill の判定に、ボクセルを壊した体積と、建物の倒壊を伝えるようにする。ステージシーンにないものは繋がない。
        /// </summary>
        private void ConnectBrilliantSkillSources()
        {
            _voxelDestructionAdapter = FindFirstObjectByType<VoxelDestructionAdapter>();
            if (_voxelDestructionAdapter == null)
            {
                UsefulLogger.LogWarning("VoxelDestructionAdapter が見つからない為、一撃の破壊の判定と、投げた言葉の着弾によるボクセルの破壊はできません。", this);
            }
            else
            {
                _voxelDestructionAdapter.SetOnCarved(OnVoxelsCarved);
            }

            foreach (var building in FindObjectsByType<VoxelBuilding>(FindObjectsSortMode.None))
            {
                _buildingSubscriptions.Add(building.RegisterOnCollapsed(OnBuildingCollapsed));
            }
        }

        /// <summary>
        /// 倒れ始めた階のピースをすべて含む範囲を、Brilliant Skill の判定に伝える。
        /// </summary>
        private void OnBuildingCollapsed(Rigidbody body)
        {
            var pieces = body.GetComponentsInChildren<VoxelPiece>();
            if (pieces.Length == 0) return;

            var bounds = pieces[0].WorldBounds;
            for (var i = 1; i < pieces.Length; i++)
            {
                bounds.Encapsulate(pieces[i].WorldBounds);
            }

            _brilliantSkillService.OnBuildingCollapsed(bounds.center, bounds.extents.magnitude);
        }

        /// <summary>
        /// 同じステージを読み直して、初めから始める。このオブジェクトは読み直しのアンロードで破棄される為、destroyCancellationToken は渡さない。
        /// </summary>
        private void Retry()
        {
            if (_sceneController == null)
            {
                UsefulLogger.LogError("IGameSceneController を受け取れなかった為、もう一度始められません。", this);
                return;
            }

            _sceneController.RetryAsync().Forget();
        }

        /// <summary>
        /// アウトゲームへ戻る。このオブジェクトは遷移中のアンロードで破棄される為、destroyCancellationToken は渡さない。
        /// </summary>
        private void BackToOutGame()
        {
            if (_sceneController == null)
            {
                UsefulLogger.LogError("IGameSceneController を受け取れなかった為、アウトゲームへ戻れません。", this);
                return;
            }

            _sceneController.GoToOutGameAsync().Forget();
        }

        /// <summary>
        /// アウトゲームで選んだステージの定義を返す。選んでいなければ（インゲームのシーンから直接再生したときなど）、既定の値のステージを作って返す。
        /// </summary>
        private StageData GetSelectedStage()
        {
            if (_progressService != null && _progressService.TryGetSelectedStage(out var stage)) return stage;

            UsefulLogger.LogWarning("ステージが選ばれていない為、既定の値のステージで始めます。", this);
            return ScriptableObject.CreateInstance<StageData>();
        }

        /// <summary>
        /// 流れの状態と「破壊済みの数 / 破壊対象の数」のあとに、破壊対象ごとの壊れ具合を並べる。
        /// </summary>
        private string GetTargetText()
        {
            _targetText.Clear();
            if (_flowState != null)
            {
                _targetText.Append(_flowState.Phase).Append(' ')
                    .Append(_flowState.DestroyedTargetCount).Append(" / ").Append(_flowState.TargetCount);
                if (_flowState.Phase == StagePhase.Failed) _targetText.Append(' ').Append(_flowState.FailReason);
            }

            foreach (var target in _stageClearAdapter.Targets)
            {
                if (target is Object unityObject && unityObject == null) continue;

                _targetText.Append(" | ");
                target.AppendStatus(_targetText);
            }

            return _targetText.ToString();
        }

        private void OnDestroy()
        {
            foreach (var subscription in _buildingSubscriptions)
            {
                subscription.Dispose();
            }

            _buildingSubscriptions.Clear();
            _flowService?.Dispose();
            _guideService?.Dispose();
        }

        public void Inject(GameProgressService instance)
        {
            _progressService = instance;
        }

        public void Inject(IGameSceneController instance)
        {
            _sceneController = instance;
        }

        public void Inject(ITimeScaleController instance)
        {
            _timeScaleController = instance;
        }

        public void Inject(IInputController instance)
        {
            _inputController = instance;
        }
    }
}
