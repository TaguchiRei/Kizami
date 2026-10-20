using Kizami.Application;
using Kizami.BlackBoard;
using Kizami.EngineAdapter;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// ステージシーンに置いたボスを探し、攻撃に、プレイヤーへダメージを与える関数と弾く関数、敵を吸収・撃破する関数と狙う相手を渡し、ボス戦の段階を進める BossBattleService と BossBattleAdapter を繋ぐ配線役。インゲームのシーンへ置く。
    /// ボスはボス戦のステージシーンにだけ置き、ボスのいないステージでは何もしない。
    /// </summary>
    public sealed class BossInitializer : InitializerBase, IInjectable<PlayerHealthService>,
        IInjectable<PlayerMovementService>
    {
        [SerializeField]
        [Tooltip("頭部のレーザーで敵を吸収・撃破する先。なければ敵を巻き込まない")]
        private EnemySpawnAdapter _enemySpawnAdapter;

        [SerializeField, Min(1)]
        [Tooltip("ボス戦の 1 周で足踏みする回数")]
        private int _stompsPerRound = 3;

        [SerializeField, Min(1)]
        [Tooltip("総攻撃の前に行う周の数")]
        private int _roundCount = 2;

        [SerializeField]
        [Tooltip("初期化の終わりにボス戦を始める")]
        private bool _beginOnInitialize = true;

        private PlayerHealthService _healthService;
        private PlayerMovementService _movementService;

        public override void Initialize(IBlackBoard blackBoard)
        {
            var battleAdapter = FindFirstObjectByType<BossBattleAdapter>();
            if (battleAdapter == null)
            {
                base.Initialize(blackBoard);
                return;
            }

            var boss = battleAdapter.gameObject;
            var applyDamage = _healthService != null ? (System.Action<int>)_healthService.ApplyDamage : null;
            var target = FindPlayerTransform();

            var body = boss.GetComponentInChildren<BossBody>(true);
            if (body != null) body.SetTarget(target);

            var shockwaves = boss.GetComponentInChildren<BossShockwaves>(true);
            if (shockwaves != null)
            {
                shockwaves.SetTarget(target);
                shockwaves.Initialize(applyDamage,
                    _movementService != null ? _movementService.RequestKnockback : null);
            }

            var finisherStrikes = boss.GetComponentInChildren<BossFinisherStrikes>(true);
            if (finisherStrikes != null)
            {
                finisherStrikes.SetTarget(target);
                finisherStrikes.Initialize(applyDamage);
            }

            var laser = boss.GetComponentInChildren<BossLaser>(true);
            if (laser != null)
            {
                laser.SetTarget(target);
                laser.Initialize(applyDamage);
            }

            var turret = boss.GetComponentInChildren<BossTurret>(true);
            if (turret != null)
            {
                turret.SetTarget(target);
                turret.Initialize(applyDamage);
            }

            var headLaser = boss.GetComponentInChildren<BossHeadLaser>(true);
            if (headLaser != null)
            {
                headLaser.SetTarget(target);
                headLaser.Initialize(applyDamage,
                    _enemySpawnAdapter != null ? _enemySpawnAdapter.AbsorbInRadius : null,
                    _enemySpawnAdapter != null
                        ? (center, radius) => _enemySpawnAdapter.DefeatInRadius(center, radius, EnemyDefeatCause.Boss)
                        : null);
            }

            var arrival = boss.GetComponentInChildren<BossArrival>(true);
            if (arrival != null)
            {
                var voxelDestruction = FindFirstObjectByType<VoxelDestructionAdapter>();
                // ボスによる破壊と撃破は、Brilliant Skill の判定に伝えない
                arrival.Initialize(
                    voxelDestruction != null
                        ? (start, end, radius) => voxelDestruction.CarveCapsuleAt(start, end, radius, false)
                        : null,
                    _enemySpawnAdapter != null
                        ? (center, radius) => _enemySpawnAdapter.DefeatInRadius(center, radius, EnemyDefeatCause.Boss)
                        : null);
            }

            // 円形の空間はボスの子にしなくてもよいので、ステージシーン全体から探す
            var arena = FindFirstObjectByType<BossArena>(FindObjectsInactive.Include);
            if (arena != null)
            {
                arena.SetTarget(target);
                arena.Initialize(applyDamage);
            }

            var battleService = new BossBattleService(blackBoard, _stompsPerRound, _roundCount,
                gameObject.scene.buildIndex);
            if (blackBoard.TryGetSceneState<EnemyBoard, IBossBattleState>(out var battleState, this))
            {
                battleAdapter.Initialize(battleState, battleService.ReportCrystalsCleared,
                    battleService.ReportArrived, battleService.ReportStomp,
                    battleService.ReportCoreAttackFinished, battleService.ReportWeaponBroken,
                    battleService.ReportAllOutFinished);
                if (_beginOnInitialize) battleService.Begin();
            }

            base.Initialize(blackBoard);
        }

        /// <summary>
        /// ボスが狙うプレイヤーの Transform を返す。見つからなければ null で、ボスは誰も狙わない。
        /// </summary>
        private Transform FindPlayerTransform()
        {
            var movementAdapter = FindFirstObjectByType<PlayerMovementAdapterBase>();
            if (movementAdapter != null) return movementAdapter.transform;

            UsefulLogger.LogError("PlayerMovementAdapterBase が見つからない為、ボスが狙う相手を渡せません。", this);
            return null;
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
