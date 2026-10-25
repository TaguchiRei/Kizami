using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージの進行の台本。ステージシーンへ置く。段階の定義を持ち、段階に入るたびに小目標・会話・操作説明（StageGuideTexts の文）を出して、物をアクティブにし、敵のスポーン位置の保留を解く。
    /// 段階を進める判断は IStageGuideController（StageGuideService）が持つ。台本は毎フレーム進め時かを尋ね、待っている破壊対象が破壊済みになったことを報告する。
    /// </summary>
    public sealed class StageGuideScript : InitializableMonoBehaviour
    {
        [SerializeField]
        [Tooltip("大目標と、段階ごとの小目標・会話の文。段階は上から順に、この台本の段階と対応する")]
        private StageGuideTexts _texts;

        [SerializeField]
        [Tooltip("最後の段階を終えたときに、ステージをクリアにするか。破壊対象のないステージ（チュートリアル）で使う")]
        private bool _clearsOnFinish;

        [SerializeField]
        [Tooltip("進行の段階。上から順に進む。最後の段階の条件を満たすと、小目標と会話を消す")]
        private GuideStep[] _steps = Array.Empty<GuideStep>();

        private IStageGuideController _controller;

        /// <summary> 今の段階の番号。まだ始めていなければ -1、全部終えたら段階の数 </summary>
        private int _stepIndex = -1;

        /// <summary> 今の段階で待っている破壊対象が破壊済みになったことを報告したか </summary>
        private bool _hasReportedTarget;

        /// <summary>
        /// 段階の定義を案内の操作面へ渡す。流れ・プレイヤーの State と、プレイヤーの出来事の通知の登録より後に、案内の操作面を作ってから呼ぶ。
        /// </summary>
        /// <param name="controller">案内の文を出し、段階を進める判断をする操作面（StageGuideService）</param>
        public void Initialize(IStageGuideController controller)
        {
            _controller = controller;
            var rules = new GuideStepRule[_steps.Length];
            for (var i = 0; i < _steps.Length; i++)
            {
                var step = _steps[i];
                rules[i] = new GuideStepRule(step.Condition.Kind, step.Condition.Count, step.Condition.Action, step.Delay);
            }

            WarnInvalidSteps();
            _controller.StartScript(rules, _clearsOnFinish, EnterStep);
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized) return;

            ReportTargetIfDestroyed();
            _controller.TickScript();
        }

        /// <summary>
        /// 今の段階が破壊対象を待つ条件で、その破壊対象が破壊済みなら、一度だけ報告する。
        /// </summary>
        private void ReportTargetIfDestroyed()
        {
            if (_hasReportedTarget || _stepIndex < 0 || _stepIndex >= _steps.Length) return;

            var condition = _steps[_stepIndex].Condition;
            if (condition.Kind != GuideConditionKind.TargetDestroyed) return;

            var target = condition.Target;
            if (target == null || !target.IsDestroyed) return;

            _hasReportedTarget = true;
            _controller.ReportTargetDestroyed();
        }

        /// <summary>
        /// 指定した段階に入る。最初の段階なら大目標を出す。小目標と会話を出し、物をアクティブにしてスポーン位置の保留を解く。段階がなければ、小目標と会話を消す。
        /// </summary>
        private void EnterStep(int index)
        {
            _stepIndex = index;
            _hasReportedTarget = false;
            if (index == 0) _controller.SetObjective(_texts != null ? _texts.Objective : string.Empty);

            if (index >= _steps.Length)
            {
                _controller.SetSubObjective(string.Empty);
                _controller.SetDialogue(string.Empty);
                _controller.SetHint(string.Empty);
                return;
            }

            var step = _steps[index];
            var subObjective = string.Empty;
            var dialogue = string.Empty;
            var hint = string.Empty;
            _texts?.TryGetStep(index, out subObjective, out dialogue, out hint);
            _controller.SetSubObjective(subObjective);
            _controller.SetDialogue(dialogue);
            _controller.SetHint(hint);
            foreach (var showObject in step.ShowObjects)
            {
                if (showObject != null) showObject.SetActive(true);
            }

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

        [SerializeField]
        [Tooltip("この段階に入ったときに、アクティブにする物（チュートリアルのダミーなど）")]
        private GameObject[] _showObjects = Array.Empty<GameObject>();

        /// <summary> この段階を終える条件 </summary>
        public GuideCondition Condition => _condition;

        /// <summary> 条件を満たしてから次の段階へ進むまでの待ち時間（秒） </summary>
        public float Delay => _delay;

        /// <summary> この段階に入ったときに、出現の保留を解くスポーン位置 </summary>
        public EnemySpawnPoint[] ReleaseSpawnPoints => _releaseSpawnPoints;

        /// <summary> この段階に入ったときに、出現の保留を解くスポーン位置の ID </summary>
        public string[] ReleaseSpawnPointIds => _releaseSpawnPointIds;

        /// <summary> この段階に入ったときに、アクティブにする物 </summary>
        public GameObject[] ShowObjects => _showObjects;
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
        [Tooltip("倒す敵の数か、破壊済みを待つ対象の数。種類が DefeatCount か DestroyedTargetCount のときに使う")]
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

        /// <summary> 倒す敵の数か、破壊済みを待つ対象の数 </summary>
        public int Count => _count;

        /// <summary> 待つプレイヤーの出来事 </summary>
        public PlayerActionKind Action => _action;
    }
}
