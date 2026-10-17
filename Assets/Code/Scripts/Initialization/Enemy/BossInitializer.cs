using Kizami.Application;
using Kizami.EngineAdapter;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// ボスの攻撃に、プレイヤーへダメージを与える関数と弾く関数を渡す配線役。ボスを置いたシーンへ置く。
    /// </summary>
    public sealed class BossInitializer : InitializerBase, IInjectable<PlayerHealthService>,
        IInjectable<PlayerMovementService>
    {
        [SerializeField] private BossShockwaves _shockwaves;

        private PlayerHealthService _healthService;
        private PlayerMovementService _movementService;

        public override void Initialize(IBlackBoard blackBoard)
        {
            if (this.IsAssigned(_shockwaves, nameof(BossShockwaves)))
            {
                _shockwaves.Initialize(_healthService != null ? _healthService.ApplyDamage : null,
                    _movementService != null ? _movementService.RequestKnockback : null);
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
