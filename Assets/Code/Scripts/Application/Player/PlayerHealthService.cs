using Kizami.BlackBoard;
using Kizami.External;
using UnityEngine;

namespace Kizami.Application
{
    /// <summary>
    /// プレイヤーの HP を管理し、ダメージを適用するユースケース。
    /// PlayerHealthState の具象インスタンスはこのクラスだけが保持する（Single Writer）。
    /// ワープ中（PlayerMovementState.Mode が Warping）に受けたダメージには軽減率を適用する。
    /// </summary>
    public sealed class PlayerHealthService
    {
        private PlayerHealthState _state;
        private IPlayerMovementState _movementState;
        private float _warpDamageReduction;

        /// <summary>
        /// PlayerHealthState を生成して PlayerBoard へ登録する。Initialize より前のダメージは無視する。
        /// </summary>
        /// <param name="playerBoard">PlayerHealthState の登録先と、PlayerMovementState の取得元</param>
        /// <param name="parameters">最大 HP とワープ中の軽減率</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public void Initialize(PlayerBoard playerBoard, PlayerParameterData parameters, int sceneId)
        {
            _state = new PlayerHealthState(parameters.MaxHealth);
            _warpDamageReduction = parameters.WarpDamageReduction;

            playerBoard.RegisterSceneState<IPlayerHealthState>(_state, sceneId);
            playerBoard.TryGetSceneState(out _movementState, out _);
        }

        /// <summary>
        /// ダメージを適用する。ワープ中は軽減率を掛け、四捨五入した値を減らす。
        /// HP が 0 のときと、ダメージが 0 以下のときは何もしない。
        /// </summary>
        /// <param name="amount">ダメージ量</param>
        public void ApplyDamage(int amount)
        {
            if (_state == null || amount <= 0 || _state.Current <= 0) return;

            var damage = amount;
            if (_movementState != null && _movementState.Mode == PlayerMoveMode.Warping)
            {
                damage = Mathf.RoundToInt(amount * (1f - _warpDamageReduction / 100f));
            }

            if (damage <= 0) return;

            _state.SetCurrent(_state.Current - damage);
        }
    }
}
