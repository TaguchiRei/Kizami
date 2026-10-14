using System;
using System.Collections.Generic;
using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.Application
{
    /// <summary>
    /// インゲームの流れ（StageFlowState）を進めるユースケース。破壊対象がすべて破壊済みになったらクリアにし、
    /// ステージの失敗条件のどれかを満たしたら失敗にする。クリアと失敗はプレイ中にだけ起こり、一度決まったら変わらない。
    /// スコアは、倒した敵 1 体につき 1 点と、クリアしたときの基礎スコアの合計。クリアタイムと被ダメージは、時間をかけて準備して壊す遊び方を損なわない為、使わない。
    /// </summary>
    public sealed class StageFlowService : IDisposable
    {
        private readonly StageFlowState _state = new();
        private readonly List<IDisposable> _subscriptions = new();
        private readonly int _clearScore;

        /// <summary>
        /// StageFlowState を StageBoard へ登録し、失敗条件の判定に使う State の変化を購読する。
        /// </summary>
        /// <param name="blackBoard">StageFlowState の登録先と、失敗条件の判定に使う State の取得元</param>
        /// <param name="stage">遊ぶステージの定義</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public StageFlowService(IBlackBoard blackBoard, StageData stage, int sceneId)
        {
            _clearScore = stage.ClearScore;

            if (!blackBoard.TryGetBoard<StageBoard>(out var stageBoard, this)) return;

            stageBoard.RegisterSceneState<IStageFlowState>(_state, sceneId);

            foreach (var condition in stage.FailConditions)
            {
                WatchFailCondition(blackBoard, condition);
            }
        }

        /// <summary>
        /// プレイを始める。開始前のときだけ、プレイ中にする。始めた時点ですべて破壊済みなら、そのままクリアにする。
        /// </summary>
        public void Begin()
        {
            if (_state.Phase != StagePhase.Ready) return;

            _state.SetPhase(StagePhase.Playing);
            TryClear();
        }

        /// <summary>
        /// 破壊対象の数と、破壊済みの数を受け取る。プレイ中にすべて破壊済みになったらクリアにする。
        /// </summary>
        /// <param name="targetCount">ステージにある破壊対象の数</param>
        /// <param name="destroyedCount">破壊済みの破壊対象の数</param>
        public void ReportTargets(int targetCount, int destroyedCount)
        {
            _state.SetTargets(targetCount, destroyedCount);
            TryClear();
        }

        /// <summary>
        /// 倒した敵の数を足し、スコアに反映する。プレイ中のときだけ数える。
        /// </summary>
        /// <param name="count">倒した敵の数</param>
        public void AddDefeats(int count)
        {
            if (_state.Phase != StagePhase.Playing || count <= 0) return;

            _state.SetDefeatCount(_state.DefeatCount + count);
            _state.SetScore(_state.Score + count);
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }

        /// <summary>
        /// 失敗条件の型に応じて、判定に使う State の変化を購読する。
        /// </summary>
        private void WatchFailCondition(IBlackBoard blackBoard, StageFailCondition condition)
        {
            switch (condition)
            {
                case PlayerDeathFailCondition:
                    if (!blackBoard.TryGetSceneState<PlayerBoard, IPlayerHealthState>(out var healthState, this)) return;

                    _subscriptions.Add(healthState.RegisterOnHealthChanged((_, current) =>
                    {
                        if (current <= 0) Fail(StageFailReason.PlayerDeath);
                    }));
                    break;
                case null:
                    break;
                default:
                    UsefulLogger.LogError($"失敗条件 {condition.GetType().Name} の判定がない為、無視します。", this);
                    break;
            }
        }

        /// <summary>
        /// プレイ中に破壊対象がすべて破壊済みなら、クリアにする。
        /// </summary>
        private void TryClear()
        {
            if (_state.Phase != StagePhase.Playing || _state.TargetCount == 0 ||
                _state.DestroyedTargetCount < _state.TargetCount) return;

            _state.SetScore(_state.Score + _clearScore);
            _state.SetPhase(StagePhase.Cleared);
            UsefulLogger.Log("すべての破壊対象が破壊済みになった為、クリアにしました。", this);
        }

        /// <summary>
        /// プレイ中のときだけ、失敗にする。
        /// </summary>
        private void Fail(StageFailReason reason)
        {
            if (_state.Phase != StagePhase.Playing) return;

            _state.SetFailReason(reason);
            _state.SetPhase(StagePhase.Failed);
            UsefulLogger.Log($"失敗条件 {reason} を満たした為、失敗にしました。", this);
        }
    }
}
