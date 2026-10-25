using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.Application
{
    /// <summary>
    /// ステージの案内（StageGuideState）の唯一の書き手。プレイヤーの出来事と敵を倒した通知を、プレイ中のときだけログにする。
    /// 通常攻撃と敵を倒した通知は、間に別のログが入らずに続いたら1行にまとめて回数を増やす。まとめるのは、ログを出しておく時間の間だけ。
    /// 大目標・小目標・会話・操作説明は、呼ばれた値をそのまま State へ書く。
    /// 台本（StageGuideScript）の段階を進める判断と、ボス戦の段階から案内の文を選ぶ判断も持つ。段階の条件を満たしたら、段階ごとの待ち時間のあとに次の段階へ進める。
    /// </summary>
    public sealed class StageGuideService : IStageGuideController, IDisposable
    {
        private const string MELEE_TEXT = "通常攻撃";
        private const string DEFEAT_TEXT = "敵を倒した";

        /// <summary> プレイヤーが動いているとみなす目標速度の大きさの2乗（m²/s²） </summary>
        private const float MOVED_SQUARE_SPEED = 0.01f;

        private readonly StageGuideState _state;
        private readonly List<IDisposable> _subscriptions = new();
        private readonly IStageFlowState _flowState;
        private readonly ISkillSlotState _slotState;
        private readonly IPlayerMovementState _movementState;
        private readonly Action _onScriptFinished;

        private IReadOnlyList<GuideStepRule> _steps;
        private bool _clearsOnFinish;
        private Action<int> _enterStep;

        /// <summary> 今の段階の番号。まだ始めていなければ -1、全部終えたら段階の数 </summary>
        private int _stepIndex = -1;

        /// <summary> 今の段階に入ってから起きたプレイヤーの出来事。種類の番号のビットで持つ </summary>
        private int _seenActions;

        /// <summary> 今の段階の条件を満たした時刻。まだ満たしていなければ負 </summary>
        private float _metTime = -1f;

        /// <summary> 今の段階に入ったときの、倒した敵の数 </summary>
        private int _defeatBaseline;

        /// <summary> 今の段階に入ってから、待っている破壊対象が破壊済みになったと報告されたか </summary>
        private bool _isTargetDestroyed;

        private IBossBattleState _bossState;
        private IReadOnlyDictionary<BossGuideTopic, GuideLine> _bossLines;

        /// <summary> 案内を出した段階のまとまり。まだ出していなければ Idle </summary>
        private BossBattlePhase _shownBossPhase = BossBattlePhase.Idle;

        /// <summary> 案内を出したときの周の番号 </summary>
        private int _shownBossRound;

        /// <summary>
        /// StageGuideState を StageBoard へ登録し、プレイヤーの出来事、敵を倒した通知、チャージの変化を購読する。
        /// StageFlowState と、プレイヤーの State・出来事の通知、敵を倒した通知の登録より後に生成する。
        /// </summary>
        /// <param name="blackBoard">StageGuideState の登録先と、流れ・スキルの枠・チャージ・出来事の通知・敵を倒した通知の取得元</param>
        /// <param name="logMaxCount">ログを持つ件数の上限</param>
        /// <param name="logLifetime">ログを出しておく時間（秒）</param>
        /// <param name="onScriptFinished">台本の最後の段階を終えたときに呼ぶ処理。クリアにする台本のときだけ呼ぶ</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public StageGuideService(IBlackBoard blackBoard, int logMaxCount, float logLifetime, Action onScriptFinished,
            int sceneId)
        {
            _state = new StageGuideState(logMaxCount, logLifetime);
            _onScriptFinished = onScriptFinished;

            if (!blackBoard.TryGetBoard<StageBoard>(out var stageBoard, this) ||
                !blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this) ||
                !blackBoard.TryGetSceneState<PlayerBoard, ISkillSlotState>(out _slotState, this) ||
                !blackBoard.TryGetSceneState<PlayerBoard, IChargeState>(out var chargeState, this) ||
                !blackBoard.TryGetEventBoard<PlayerEventBoard>(out var eventBoard, this) ||
                !blackBoard.TryGetSceneEvent<EnemyEventBoard, IEnemyDefeatEvents>(out var defeatEvents, this)) return;

            if (!eventBoard.TryGetSceneEvent<IPlayerActionEvents>(out var actionEvents, out _))
            {
                UsefulLogger.LogError("IPlayerActionEvents が未登録です。PlayerInitializer の初期化順を確認してください。", this);
                return;
            }

            stageBoard.RegisterSceneState<IStageGuideState>(_state, sceneId);
            // 動いたかの条件にだけ使うので、取れなくても案内とログは止めない
            blackBoard.TryGetSceneState<PlayerBoard, IPlayerMovementState>(out _movementState, this);
            _subscriptions.Add(actionEvents.OnAction.Register(OnAction));
            _subscriptions.Add(defeatEvents.OnDefeated.Register(OnDefeated));
            _subscriptions.Add(chargeState.RegisterOnChargeChanged(OnChargeChanged));

            // BossBattleState はボスのいるシーンでだけ、ボスの Initializer が登録するので、登録を待ち受けて拾う
            if (blackBoard.TryGetBoard<EnemyBoard>(out var enemyBoard, this))
            {
                _subscriptions.Add(enemyBoard.SubscribeStateRegister<IBossBattleState>(
                    () =>
                    {
                        if (enemyBoard.TryGetSceneState<IBossBattleState>(out var bossState, out _)) _bossState = bossState;
                    },
                    invokeIfRegistered: true));
            }
        }

        /// <summary>
        /// 案内を分ける段階のまとまりに直す。直接戦闘の3つの段階は、やることが同じなので Arriving にまとめる。
        /// </summary>
        private static BossBattlePhase ToGuidePhase(BossBattlePhase phase)
        {
            return phase is BossBattlePhase.Stomping or BossBattlePhase.CoreExposed ? BossBattlePhase.Arriving : phase;
        }

        /// <summary>
        /// 段階のまとまりと周の番号から、案内の話題を選ぶ。直接戦闘は 1 周目と 2 周目以降で分ける。
        /// </summary>
        private static bool TryGetBossTopic(BossBattlePhase phase, int round, out BossGuideTopic topic)
        {
            switch (phase)
            {
                case BossBattlePhase.Arriving:
                    topic = round <= 1 ? BossGuideTopic.FirstArrival : BossGuideTopic.Return;
                    return true;
                case BossBattlePhase.Down:
                    topic = BossGuideTopic.Down;
                    return true;
                case BossBattlePhase.Crystal:
                    topic = BossGuideTopic.Crystal;
                    return true;
                case BossBattlePhase.Final:
                    topic = BossGuideTopic.Final;
                    return true;
                case BossBattlePhase.Stopped:
                    topic = BossGuideTopic.Stopped;
                    return true;
                default:
                    topic = default;
                    return false;
            }
        }

        /// <summary>
        /// 指定した段階に入り、条件の基準を取り直して、台本に段階に入ったことを伝える。全部終えたら、クリアにする台本ならクリアにする。
        /// </summary>
        private void EnterStep(int index)
        {
            _stepIndex = index;
            _metTime = -1f;
            _seenActions = 0;
            _isTargetDestroyed = false;
            _defeatBaseline = _flowState.DefeatCount;
            _enterStep?.Invoke(index);
            if (index >= _steps.Count && _clearsOnFinish) _onScriptFinished?.Invoke();
        }

        private bool IsMet(in GuideStepRule step)
        {
            switch (step.Kind)
            {
                case GuideConditionKind.TargetDestroyed:
                    return _isTargetDestroyed;
                case GuideConditionKind.DestroyedTargetCount:
                    return _flowState.DestroyedTargetCount >= Mathf.Max(1, step.Count);
                case GuideConditionKind.DefeatCount:
                    return _flowState.DefeatCount - _defeatBaseline >= Mathf.Max(1, step.Count);
                case GuideConditionKind.PlayerAction:
                    return (_seenActions & (1 << (int)step.Action)) != 0;
                case GuideConditionKind.PlayerMoved:
                    return _movementState != null && _movementState.TargetVelocity.sqrMagnitude > MOVED_SQUARE_SPEED;
                case GuideConditionKind.None:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsPlaying()
        {
            return _flowState != null && _flowState.Phase == StagePhase.Playing;
        }

        /// <summary>
        /// ログの末尾が同じ文でまだ出ていれば、その回数を増やす。そうでなければ新しい行を足す。
        /// </summary>
        private void AppendLog(string text, int count, bool canMerge)
        {
            var now = Time.unscaledTime;
            var logs = _state.Logs;
            if (canMerge && logs.Count > 0)
            {
                var last = logs[logs.Count - 1];
                if (last.Text == text && now - last.Time <= _state.LogLifetime)
                {
                    _state.UpdateLastLog(last.Count + count, now);
                    return;
                }
            }

            _state.AppendLog(text, count, now);
        }

        /// <summary>
        /// 倒した敵の数をログにする。プレイ中のときだけ数える。
        /// </summary>
        private void OnDefeated(EnemyDefeatNotice notice)
        {
            if (notice.Count <= 0 || !IsPlaying()) return;

            AppendLog(DEFEAT_TEXT, notice.Count, true);
        }

        private void OnAction(PlayerActionNotice notice)
        {
            // 台本の条件は、プレイ中かに関わらず、段階に入ってから起きた出来事を数える
            _seenActions |= 1 << (int)notice.Kind;
            if (!IsPlaying()) return;

            switch (notice.Kind)
            {
                case PlayerActionKind.SkillUsed:
                    AppendLog($"{notice.Name}を発動", 1, false);
                    break;
                case PlayerActionKind.ChargeSpent:
                    AppendLog($"エネルギー -{notice.Amount}", 1, false);
                    break;
                case PlayerActionKind.MeleeSwung:
                    AppendLog(MELEE_TEXT, 1, true);
                    break;
            }
        }

        /// <summary>
        /// チャージが、装備しているスキルの消費量に届いたら、そのスキルが使えるようになったことをログにする。
        /// </summary>
        private void OnChargeChanged(int previous, int current)
        {
            if (!IsPlaying()) return;

            for (var slot = 0; slot < _slotState.SlotCount; slot++)
            {
                if (!_slotState.TryGetCost(slot, out var cost)) continue;
                if (previous < cost && current >= cost) AppendLog($"{_slotState.GetName(slot)}が使用可能", 1, false);
            }
        }

        /// <summary>
        /// 大目標を設定する。
        /// </summary>
        /// <param name="objective">大目標の文。空なら出さない</param>
        public void SetObjective(string objective)
        {
            _state.SetObjective(objective);
        }

        /// <summary>
        /// 小目標を設定する。
        /// </summary>
        /// <param name="subObjective">小目標の文。空なら出さない</param>
        public void SetSubObjective(string subObjective)
        {
            _state.SetSubObjective(subObjective);
        }

        /// <summary>
        /// 会話を設定する。
        /// </summary>
        /// <param name="dialogue">会話の文。空なら出さない</param>
        public void SetDialogue(string dialogue)
        {
            _state.SetDialogue(dialogue);
        }

        /// <summary>
        /// 操作説明を設定する。
        /// </summary>
        /// <param name="hint">操作説明の文。空なら出さない</param>
        public void SetHint(string hint)
        {
            _state.SetHint(hint);
        }

        public void StartScript(IReadOnlyList<GuideStepRule> steps, bool clearsOnFinish, Action<int> enterStep)
        {
            _steps = steps;
            _clearsOnFinish = clearsOnFinish;
            _enterStep = enterStep;
            _stepIndex = -1;
        }

        public void TickScript()
        {
            if (_steps == null || !IsPlaying()) return;

            if (_stepIndex < 0)
            {
                EnterStep(0);
                return;
            }

            if (_stepIndex >= _steps.Count) return;

            // 移動の入力のように一瞬しか成り立たない条件も、一度満たしたら待ち時間のあいだ保つ
            if (_metTime < 0f && IsMet(_steps[_stepIndex])) _metTime = Time.time;
            if (_metTime < 0f || Time.time - _metTime < _steps[_stepIndex].Delay) return;

            EnterStep(_stepIndex + 1);
        }

        public void ReportTargetDestroyed()
        {
            _isTargetDestroyed = true;
        }

        public void StartBossGuide(string objective, IReadOnlyDictionary<BossGuideTopic, GuideLine> lines)
        {
            _bossLines = lines;
            SetObjective(objective);
        }

        public void TickBossGuide()
        {
            if (_bossLines == null || _bossState == null) return;

            var phase = ToGuidePhase(_bossState.Phase);
            if (phase == BossBattlePhase.Idle || (phase == _shownBossPhase && _bossState.Round == _shownBossRound)) return;

            _shownBossPhase = phase;
            _shownBossRound = _bossState.Round;
            if (TryGetBossTopic(phase, _bossState.Round, out var topic) && _bossLines.TryGetValue(topic, out var line))
            {
                SetSubObjective(line.SubObjective);
                SetDialogue(line.Dialogue);
                SetHint(line.Hint);
            }
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}
