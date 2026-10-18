using Kizami.Application;
using Kizami.BlackBoard;
using Kizami.EngineAdapter;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// ボスの攻撃に、プレイヤーへダメージを与える関数と弾く関数を渡し、直接戦闘の段階を進める BossBattleService と BossBattleAdapter を繋ぐ配線役。ボスを置いたシーンへ置く。
    /// </summary>
    public sealed class BossInitializer : InitializerBase, IInjectable<PlayerHealthService>,
        IInjectable<PlayerMovementService>
    {
        [SerializeField] private BossShockwaves _shockwaves;
        [SerializeField] private BossMissiles _missiles;
        [SerializeField] private BossLaser _laser;
        [SerializeField] private BossTurret _turret;
        [SerializeField] private BossArena _arena;
        [SerializeField] private BossBattleAdapter _battleAdapter;

        [SerializeField, Min(1)]
        [Tooltip("直接戦闘の 1 周で足踏みする回数")]
        private int _stompsPerRound = 3;

        [SerializeField, Min(1)]
        [Tooltip("総攻撃の前に行う周の数")]
        private int _roundCount = 2;

        [SerializeField]
        [Tooltip("初期化の終わりに直接戦闘を始める")]
        private bool _beginOnInitialize = true;

        private PlayerHealthService _healthService;
        private PlayerMovementService _movementService;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (this.IsAssigned(_shockwaves, nameof(BossShockwaves)))
            {
                _shockwaves.Initialize(_healthService != null ? _healthService.ApplyDamage : null,
                    _movementService != null ? _movementService.RequestKnockback : null);
            }

            if (this.IsAssigned(_missiles, nameof(BossMissiles)))
            {
                _missiles.Initialize(_healthService != null ? _healthService.ApplyDamage : null);
            }

            if (this.IsAssigned(_laser, nameof(BossLaser)))
            {
                _laser.Initialize(_healthService != null ? _healthService.ApplyDamage : null);
            }

            if (this.IsAssigned(_turret, nameof(BossTurret)))
            {
                _turret.Initialize(_healthService != null ? _healthService.ApplyDamage : null);
            }

            if (this.IsAssigned(_arena, nameof(BossArena)))
            {
                _arena.Initialize(_healthService != null ? _healthService.ApplyDamage : null);
            }

            if (this.IsAssigned(_battleAdapter, nameof(BossBattleAdapter)))
            {
                var battleService = new BossBattleService(blackBoard, _stompsPerRound, _roundCount,
                    gameObject.scene.buildIndex);
                if (blackBoard.TryGetSceneState<EnemyBoard, IBossBattleState>(out var battleState, this))
                {
                    _battleAdapter.Initialize(battleState, battleService.ReportStomp,
                        battleService.ReportCoreAttackFinished, battleService.ReportWeaponBroken,
                        battleService.ReportAllOutFinished);
                    if (_beginOnInitialize) battleService.Begin();
                }
            }

            base.Initialize(blackBoard);
        }

        public void Inject(PlayerHealthService instance)
        {
            _healthService = instance;
        }

        public void Inject(PlayerMovementService instance)
        {
            _movementService = instance;
        }
    }
}
