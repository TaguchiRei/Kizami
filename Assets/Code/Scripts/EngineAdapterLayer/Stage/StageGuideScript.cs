using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージの進行の台本。ステージシーンへ置く。段階を順に進め、段階に入るたびに小目標・会話・操作説明（StageGuideTexts の文）を出して、敵のスポーン位置の保留を解く。
    /// 段階の条件を満たしたら、段階ごとの待ち時間のあとに次の段階へ進む。1回の Update で進むのは1段階まで。プレイ中のときだけ進める。
    /// 画面に出す文は、初期化で受け取った関数で Application へ渡す。
    /// </summary>
    public sealed class StageGuideScript : InitializableMonoBehaviour
    {
        /// <summary> プレイヤーが動いているとみなす目標速度の大きさの2乗（m²/s²） </summary>
        private const float MOVED_SQUARE_SPEED = 0.01f;

        [SerializeField]
        [Tooltip("大目標と、段階ごとの小目標・会話の文。段階は上から順に、この台本の段階と対応する")]
        private StageGuideTexts _texts;

        [SerializeField]
        [Tooltip("最後の段階を終えたときに、ステージをクリアにするか。破壊対象のないステージ（チュートリアル）で使う")]
        private bool _clearsOnFinish;

        [SerializeField]
        [Tooltip("進行の段階。上から順に進む。最後の段階の条件を満たすと、小目標と会話を消す")]
        private GuideStep[] _steps = Array.Empty<GuideStep>();

        private IDisposable _actionSubscription;
        private IStageFlowState _flowState;
        private IPlayerMovementState _movementState;
        private Action<string> _setObjective;
        private Action<string> _setSubObjective;
        private Action<string> _setDialogue;
        private Action<string> _setHint;
        private Action _onFinished;

        /// <summary> 今の段階の番号。まだ始めていなければ -1、全部終えたら段階の数 </summary>
        private int _stepIndex = -1;

        /// <summary> 今の段階に入ってから起きたプレイヤーの出来事。種類の番号のビットで持つ </summary>
        private int _seenActions;

        /// <summary> 今の段階の条件を満たした時刻。まだ満たしていなければ負 </summary>
        private float _metTime = -1f;

        /// <summary> 今の段階に入ったときの、倒した敵の数 </summary>
        private int _defeatBaseline;

        /// <summary>
        /// 必要な State を取得し、プレイヤーの出来事を購読する。流れ・プレイヤーの State と、プレイヤーの出来事の通知の登録より後に呼ぶ。
        /// </summary>
        /// <param name="blackBoard">流れ・プレイヤーの State と、プレイヤーの出来事の通知の取得元</param>
        /// <param name="setObjective">大目標を画面に出す関数</param>
        /// <param name="setSubObjective">小目標を画面に出す関数</param>
        /// <param name="setDialogue">会話を画面に出す関数</param>
        /// <param name="setHint">操作説明を画面に出す関数</param>
        /// <param name="onFinished">最後の段階を終えたときに呼ぶ処理。クリアにする設定のときだけ呼ぶ</param>
        public void Initialize(IBlackBoard blackBoard, Action<string> setObjective, Action<string> setSubObjective,
            Action<string> setDialogue, Action<string> setHint, Action onFinished)
        {
            if (!blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this) ||
                !blackBoard.TryGetSceneState<PlayerBoard, IPlayerMovementState>(out _movementState, this) ||
                !blackBoard.TryGetEventBoard<PlayerEventBoard>(out var eventBoard, this)) return;

            if (!eventBoard.TryGetSceneEvent<IPlayerActionEvents>(out var actionEvents, out _))
            {
                UsefulLogger.LogError("IPlayerActionEvents が未登録です。PlayerInitializer の初期化順を確認してください。", this);
                return;
            }

            _setObjective = setObjective;
            _setSubObjective = setSubObjective;
            _setDialogue = setDialogue;
            _setHint = setHint;
            _onFinished = onFinished;
            _actionSubscription = actionEvents.OnAction.Register(OnAction);
            WarnInvalidSteps();
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized || _flowState.Phase != StagePhase.Playing) return;

            if (_stepIndex < 0)
            {
                _setObjective?.Invoke(_texts != null ? _texts.Objective : string.Empty);
                EnterStep(0);
                return;
            }

            if (_stepIndex >= _steps.Length) return;

            // 移動の入力のように一瞬しか成り立たない条件も、一度満たしたら待ち時間のあいだ保つ
            if (_metTime < 0f && IsMet(_steps[_stepIndex].Condition)) _metTime = Time.time;
            if (_metTime < 0f || Time.time - _metTime < _steps[_stepIndex].Delay) return;

            EnterStep(_stepIndex + 1);
        }

        private void OnDestroy()
        {
            _actionSubscription?.Dispose();
        }

        /// <summary>
        /// 指定した段階に入る。小目標と会話を出し、スポーン位置の保留を解いて、条件の基準を取り直す。段階がなければ、小目標と会話を消す。
        /// </summary>
        private void EnterStep(int index)
        {
            _stepIndex = index;
            _metTime = -1f;
            _seenActions = 0;
            _defeatBaseline = _flowState.DefeatCount;

            if (index >= _steps.Length)
            {
                _setSubObjective?.Invoke(string.Empty);
                _setDialogue?.Invoke(string.Empty);
                _setHint?.Invoke(string.Empty);
                if (_clearsOnFinish) _onFinished?.Invoke();
                return;
            }

            var step = _steps[index];
            var subObjective = string.Empty;
            var dialogue = string.Empty;
            var hint = string.Empty;
            _texts?.TryGetStep(index, out subObjective, out dialogue, out hint);
            _setSubObjective?.Invoke(subObjective);
            _setDialogue?.Invoke(dialogue);
            _setHint?.Invoke(hint);
            foreach (var spawnPoint in step.ReleaseSpawnPoints)
            {
                if (spawnPoint != null) spawnPoint.Release();
            }

            foreach (var id in step.ReleaseSpawnPointIds)
            {
                if (StageGimmick.TryFind(id, out var gimmick) && gimmick.TryGetComponent<EnemySpawnPoint>(out var spawnPoint))
                {
                    spawnPoint.Release();
                }
                else
                {
                    UsefulLogger.LogWarning($"ID '{id}' のスポーン位置が見つからない為、出現の保留を解けません。", this);
                }
            }
        }

        private bool IsMet(in GuideCondition condition)
        {
            switch (condition.Kind)
            {
                case GuideConditionKind.TargetDestroyed:
                    var target = condition.Target;
                    return target != null && target.IsDestroyed;
                case GuideConditionKind.DefeatCount:
                    return _flowState.DefeatCount - _defeatBaseline >= Mathf.Max(1, condition.Count);
                case GuideConditionKind.PlayerAction:
                    return (_seenActions & (1 << (int)condition.Action)) != 0;
                case GuideConditionKind.PlayerMoved:
                    return _movementState.TargetVelocity.sqrMagnitude > MOVED_SQUARE_SPEED;
                case GuideConditionKind.None:
                    return true;
                default:
                    return false;
            }
        }

        private void OnAction(PlayerActionNotice notice)
        {
            _seenActions |= 1 << (int)notice.Kind;
        }

        /// <summary>
        /// 条件に必要な参照がない段階と、文の段階の数が台本と合わないことを警告する。参照がない段階は進まない。
        /// </summary>
        private void WarnInvalidSteps()
        {
            if (_texts == null)
            {
                UsefulLogger.LogWarning("文（StageGuideTexts）が設定されていない為、大目標・小目標・会話を出しません。", this);
            }
            else if (_texts.StepCount != _steps.Length)
            {
                UsefulLogger.LogWarning(
                    $"文の段階の数（{_texts.StepCount}）が台本の段階の数（{_steps.Length}）と違います。足りない段階は、小目標と会話を出しません。", this);
            }

            for (var i = 0; i < _steps.Length; i++)
            {
                var condition = _steps[i].Condition;
                if (condition.Kind == GuideConditionKind.TargetDestroyed && !condition.HasTargetReference)
                {
                    UsefulLogger.LogWarning($"台本の段階 {i} は破壊済みを待つ条件ですが、破壊対象も ID も設定されていないか、破壊対象が IDestructionTarget ではない為、進みません。", this);
                }
            }
        }
    }

    /// <summary>
    /// 台本の段階1つ。条件を満たしたら次の段階へ進む。小目標・会話・操作説明の文は、StageGuideTexts の同じ番号の段階にある。
    /// </summary>
    [Serializable]
    public sealed class GuideStep
    {
        [SerializeField]
        [Tooltip("この段階を終える条件")]
        private GuideCondition _condition;

        [SerializeField, Min(0f)]
        [Tooltip("条件を満たしてから次の段階へ進むまでの待ち時間（秒）")]
        private float _delay;

        [SerializeField]
        [Tooltip("この段階に入ったときに、出現の保留を解くスポーン位置")]
        private EnemySpawnPoint[] _releaseSpawnPoints = Array.Empty<EnemySpawnPoint>();

        [SerializeField]
        [Tooltip("この段階に入ったときに、出現の保留を解くスポーン位置の ID。別のシーンにあるスポーン位置はこちらで指定する")]
        private string[] _releaseSpawnPointIds = Array.Empty<string>();

        /// <summary> この段階を終える条件 </summary>
        public GuideCondition Condition => _condition;

        /// <summary> 条件を満たしてから次の段階へ進むまでの待ち時間（秒） </summary>
        public float Delay => _delay;

        /// <summary> この段階に入ったときに、出現の保留を解くスポーン位置 </summary>
        public EnemySpawnPoint[] ReleaseSpawnPoints => _releaseSpawnPoints;

        /// <summary> この段階に入ったときに、出現の保留を解くスポーン位置の ID </summary>
        public string[] ReleaseSpawnPointIds => _releaseSpawnPointIds;
    }

    /// <summary>
    /// 段階を終える条件の種類。
    /// </summary>
    public enum GuideConditionKind
    {
        /// <summary> 指定した破壊対象が破壊済みになった </summary>
        TargetDestroyed,

        /// <summary> この段階に入ってから、指定した数の敵を倒した </summary>
        DefeatCount,

        /// <summary> この段階に入ってから、指定したプレイヤーの出来事が起きた </summary>
        PlayerAction,

        /// <summary> プレイヤーが移動の入力をしている </summary>
        PlayerMoved,

        /// <summary> 条件はなく、段階に入ってすぐ満たす。待ち時間だけで進める </summary>
        None
    }

    /// <summary>
    /// 段階を終える条件。種類に応じた項目だけを使う。
    /// </summary>
    [Serializable]
    public struct GuideCondition
    {
        [SerializeField]
        [Tooltip("条件の種類")]
        private GuideConditionKind _kind;

        [SerializeField]
        [Tooltip("破壊済みを待つ破壊対象（IDestructionTarget を実装したコンポーネント）。種類が TargetDestroyed のときに使う")]
        private MonoBehaviour _target;

        [SerializeField]
        [Tooltip("破壊済みを待つ破壊対象のギミック ID。別のシーンにある破壊対象はこちらで指定する。上の破壊対象が設定されていればそちらを使う")]
        private string _targetId;

        [SerializeField, Min(1)]
        [Tooltip("倒す敵の数。種類が DefeatCount のときに使う")]
        private int _count;

        [SerializeField]
        [Tooltip("待つプレイヤーの出来事。種類が PlayerAction のときに使う")]
        private PlayerActionKind _action;

        /// <summary> 条件の種類 </summary>
        public GuideConditionKind Kind => _kind;

        /// <summary> 破壊済みを待つ破壊対象 </summary>
        public IDestructionTarget Target
        {
            get
            {
                if (_target != null) return _target as IDestructionTarget;

                return !string.IsNullOrEmpty(_targetId) && StageGimmick.TryFind(_targetId, out var gimmick)
                    ? gimmick.GetComponent<IDestructionTarget>()
                    : null;
            }
        }

        /// <summary> 破壊対象か ID のどちらかが設定されていて、設定されている破壊対象が IDestructionTarget か </summary>
        public bool HasTargetReference =>
            _target != null ? _target is IDestructionTarget : !string.IsNullOrEmpty(_targetId);

        /// <summary> 倒す敵の数 </summary>
        public int Count => _count;

        /// <summary> 待つプレイヤーの出来事 </summary>
        public PlayerActionKind Action => _action;
    }
}
