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
    /// </summary>
    public sealed class StageGuideService : IDisposable
    {
        private const string MELEE_TEXT = "通常攻撃";
        private const string DEFEAT_TEXT = "敵を倒した";

        private readonly StageGuideState _state;
        private readonly List<IDisposable> _subscriptions = new();
        private readonly IStageFlowState _flowState;
        private readonly ISkillSlotState _slotState;

        /// <summary>
        /// StageGuideState を StageBoard へ登録し、プレイヤーの出来事、敵を倒した通知、チャージの変化を購読する。
        /// StageFlowState と、プレイヤーの State・出来事の通知、敵を倒した通知の登録より後に生成する。
        /// </summary>
        /// <param name="blackBoard">StageGuideState の登録先と、流れ・スキルの枠・チャージ・出来事の通知・敵を倒した通知の取得元</param>
        /// <param name="logMaxCount">ログを持つ件数の上限</param>
        /// <param name="logLifetime">ログを出しておく時間（秒）</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public StageGuideService(IBlackBoard blackBoard, int logMaxCount, float logLifetime, int sceneId)
        {
            _state = new StageGuideState(logMaxCount, logLifetime);

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
            _subscriptions.Add(actionEvents.OnAction.Register(OnAction));
            _subscriptions.Add(defeatEvents.OnDefeated.Register(OnDefeated));
            _subscriptions.Add(chargeState.RegisterOnChargeChanged(OnChargeChanged));
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
