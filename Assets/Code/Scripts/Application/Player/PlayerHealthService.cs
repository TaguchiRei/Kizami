using Kizami.BlackBoard;
using Kizami.External;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// プレイヤーの HP を管理し、ダメージと回復を適用するユースケース。
    /// ワープ中（PlayerMovementState.Mode が Warping）に受けたダメージには軽減率を適用する。
    /// インゲームの流れ（StageFlowState）がある間は、プレイ中に受けたダメージだけを適用する。開始の表示中とクリア・失敗のあとに HP を変えない為。
    /// </summary>
    public sealed class PlayerHealthService
    {
        private PlayerHealthState _state;
        private IPlayerMovementState _movementState;
        private IStageFlowState _flowState;
        private IBlackBoard _blackBoard;
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
            _blackBoard = blackBoard;
        }

        /// <summary>
        /// ダメージを適用する。ワープ中は軽減率を掛け、四捨五入した値を減らす。
        /// HP が残っていて、ダメージが正で、インゲームの流れがプレイ中か流れのないシーンのときだけ適用する。
        /// </summary>
        /// <param name="amount">ダメージ量</param>
        public void ApplyDamage(int amount)
        {
            if (_state == null || amount <= 0 || _state.Current <= 0) return;
            if (!IsDamageablePhase()) return;

            var damage = amount;
            if (_movementState != null && _movementState.Mode == PlayerMoveMode.Warping)
            {
                damage = Mathf.RoundToInt(amount * (1f - _warpDamageReduction / 100f));
            }

            if (damage <= 0) return;

            _state.SetCurrent(_state.Current - damage);
        }

        /// <summary>
        /// HP を回復する。最大 HP を超えた分は捨てる。HP が 0 のときと、回復量が 0 以下のときは何もしない。
        /// </summary>
        /// <param name="amount">回復量</param>
        public void Heal(int amount)
        {
            if (_state == null || amount <= 0 || _state.Current <= 0) return;

            _state.SetCurrent(_state.Current + amount);
        }

        /// <summary>
        /// インゲームの流れがプレイ中か、流れのないシーン（開発用のシーンなど）かを返す。
        /// StageFlowState はプレイヤーより後に初期化される為、最初のダメージのときに取得する。
        /// </summary>
        private bool IsDamageablePhase()
        {
            if (_blackBoard != null)
            {
                _blackBoard.TryGetSceneState<StageBoard, IStageFlowState>(out _flowState, this);
                _blackBoard = null;
            }

            return _flowState == null || _flowState.Phase == StagePhase.Playing;
        }
    }
}
