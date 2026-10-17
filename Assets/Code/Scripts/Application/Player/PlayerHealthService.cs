using System;
using Kizami.BlackBoard;
using Kizami.External;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// プレイヤーの HP を管理し、ダメージと回復を適用するユースケース。
    /// ワープ中（PlayerMovementState.Mode が Warping）に受けたダメージには軽減率を適用する。文字をつかんで狙っている間（Aiming）はダメージを受けない。
    /// インゲームの流れ（StageFlowState）がある間は、プレイ中に受けたダメージだけを適用する。開始の表示中とクリア・失敗のあとに HP を変えない為。
    /// </summary>
    public sealed class PlayerHealthService : IDisposable
    {
        private PlayerHealthState _state;
        private IPlayerMovementState _movementState;
        private IStageFlowState _flowState;
        private IDisposable _flowStateWaiter;
        private float _warpDamageReduction;

        /// <summary>
        /// PlayerHealthState を生成して PlayerBoard へ登録する。Initialize より前のダメージは無視する。
        /// </summary>
        /// <param name="blackBoard">PlayerHealthState の登録先と、PlayerMovementState の取得元</param>
        /// <param name="parameters">最大 HP とワープ中の軽減率</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public void Initialize(IBlackBoard blackBoard, PlayerParameterData parameters, int sceneId)
        {
            if (!blackBoard.TryGetBoard<PlayerBoard>(out var playerBoard, this)) return;

            _state = new PlayerHealthState(parameters.MaxHealth);
            _warpDamageReduction = parameters.WarpDamageReduction;

            playerBoard.RegisterSceneState<IPlayerHealthState>(_state, sceneId);
            blackBoard.TryGetSceneState<PlayerBoard, IPlayerMovementState>(out _movementState, this);

            // StageFlowState はプレイヤーより後に登録される為、登録を待って受け取る
            if (!blackBoard.TryGetBoard<StageBoard>(out var stageBoard, this)) return;

            _flowStateWaiter = stageBoard.SubscribeStateRegister<IStageFlowState>(
                () =>
                {
                    if (stageBoard.TryGetSceneState<IStageFlowState>(out var state, out _))
                    {
                        _flowState = state;
                    }
                });
        }

        /// <summary>
        /// ダメージを適用する。ワープ中は軽減率を掛け、四捨五入した値を減らす。
        /// HP が残っていて、ダメージが正で、狙っている間（Aiming）でなく、インゲームの流れがプレイ中か流れのないシーンのときだけ適用する。
        /// </summary>
        /// <param name="amount">ダメージ量</param>
        public void ApplyDamage(int amount)
        {
            if (_state == null || amount <= 0 || _state.Current <= 0) return;
            if (!IsDamageablePhase()) return;
            if (_movementState != null && _movementState.Mode == PlayerMoveMode.Aiming) return;

            var damage = amount;
            if (_movementState != null && _movementState.Mode == PlayerMoveMode.Warping)
            {
                damage = Mathf.RoundToInt(amount * (1f - _warpDamageReduction / 100f));
            }

            if (damage <= 0) return;

            _state.SetCurrent(_state.Current - damage);
        }

        /// <summary>
        /// HP を回復する。最大 HP を超えた分は捨てる。HP が0のときと、回復量が0以下のときは何もしない。
        /// </summary>
        /// <param name="amount">回復量</param>
        public void Heal(int amount)
        {
            if (_state == null || amount <= 0 || _state.Current <= 0) return;

            _state.SetCurrent(_state.Current + amount);
        }

        /// <summary>
        /// インゲームの流れがプレイ中か、流れのないシーン（開発用のシーンなど）かを返す。
        /// </summary>
        private bool IsDamageablePhase()
        {
            return _flowState == null || _flowState.Phase == StagePhase.Playing;
        }

        public void Dispose()
        {
            _flowStateWaiter?.Dispose();
        }
    }
}
