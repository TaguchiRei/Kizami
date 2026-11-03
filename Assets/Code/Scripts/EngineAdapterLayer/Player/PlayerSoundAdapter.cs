using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// プレイヤーの出来事と状態の変化を受けて、効果音を鳴らす Adapter。
    /// スキル・近接を振る・ジャンプ・壁走りの開始は出来事の通知で、被ダメージは HP の減少で、スローモードの開始と終了はスローモードの状態の変化で鳴らす。
    /// 着地と足音は移動の Adapter が、近接が当たった音は近接切断の Adapter が鳴らす。
    /// </summary>
    public sealed class PlayerSoundAdapter : MonoBehaviour
    {
        private PlayerMovementAdapterBase _movementAdapter;
        private ISlowModeState _slowModeState;
        private IDisposable _actionSubscription;
        private IDisposable _healthSubscription;
        private bool _wasSlowModeActive;

        /// <summary>
        /// PlayerActionEvents・PlayerHealthState・SlowModeState の登録より後に呼ぶこと。
        /// </summary>
        /// <param name="blackBoard">出来事と状態の取得元</param>
        /// <param name="movementAdapter">音を鳴らす位置にするプレイヤーの位置の取得元</param>
        public void Initialize(IBlackBoard blackBoard, PlayerMovementAdapterBase movementAdapter)
        {
            _movementAdapter = movementAdapter;

            if (blackBoard.TryGetSceneEvent<PlayerEventBoard, IPlayerActionEvents>(out var actionEvents, this))
            {
                _actionSubscription = actionEvents.OnAction.Register(OnAction);
            }

            if (blackBoard.TryGetSceneState<PlayerBoard, IPlayerHealthState>(out var healthState, this))
            {
                _healthSubscription = healthState.RegisterOnHealthChanged(OnHealthChanged);
            }

            if (blackBoard.TryGetSceneState<PlayerBoard, ISlowModeState>(out _slowModeState, this))
            {
                _wasSlowModeActive = _slowModeState.IsActive;
            }
        }

        private void Update()
        {
            if (_slowModeState == null || _slowModeState.IsActive == _wasSlowModeActive) return;

            _wasSlowModeActive = _slowModeState.IsActive;
            Play(_wasSlowModeActive ? SoundEffectKind.SlowModeStart : SoundEffectKind.SlowModeEnd);
        }

        private void OnDestroy()
        {
            _actionSubscription?.Dispose();
            _healthSubscription?.Dispose();
        }

        /// <summary>
        /// 出来事に対応する効果音を鳴らす。壁蹴りはジャンプと同じ音にする。回復のスキルの音は、手に入れる手段が決まるまで鳴らさない。
        /// </summary>
        private void OnAction(PlayerActionNotice notice)
        {
            switch (notice.Kind)
            {
                case PlayerActionKind.SkillUsed:
                    PlaySkill(notice.SkillEffect);
                    break;
                case PlayerActionKind.MeleeSwung:
                    Play(SoundEffectKind.PlayerSwing);
                    break;
                case PlayerActionKind.Jumped:
                case PlayerActionKind.WallKicked:
                    Play(SoundEffectKind.PlayerJump);
                    break;
                case PlayerActionKind.WallRunStarted:
                    Play(SoundEffectKind.PlayerWallLatch);
                    break;
            }
        }

        private void PlaySkill(SkillEffectKind effect)
        {
            switch (effect)
            {
                case SkillEffectKind.Common:
                    Play(SoundEffectKind.PlayerSkill);
                    break;
                case SkillEffectKind.Rush:
                    Play(SoundEffectKind.PlayerSkillRush);
                    break;
                case SkillEffectKind.SkyBeam:
                    Play(SoundEffectKind.PlayerSkillUtenkougou);
                    break;
            }
        }

        private void OnHealthChanged(int previous, int current)
        {
            if (current < previous) Play(SoundEffectKind.PlayerDamage);
        }

        private void Play(SoundEffectKind kind)
        {
            SoundPlayer.Play(kind, _movementAdapter != null ? _movementAdapter.Position : transform.position);
        }
    }
}
