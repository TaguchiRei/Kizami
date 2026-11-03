using Kizami.Application;
using Kizami.BlackBoard;
using Kizami.EngineAdapter;
using Kizami.EngineAdapter.Voxel;
using Kizami.External;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// ステージシーンに置いたボスを探し、攻撃に、プレイヤーへダメージを与える関数と弾く関数、敵を吸収・撃破する関数と狙う相手を渡し、ボス戦の段階を進める BossBattleService と BossBattleAdapter を繋ぐ配線役。インゲームのシーンへ置く。
    /// ボスはボス戦のステージシーンにだけ置き、ボスのいないステージでは何もしない。
    /// 直接戦闘で投げる塔（BossTowerThrow）もボスに付ける。
    /// 次に向かう場所を選ぶ BossTargetSelector と、段階ごとの案内の BossGuideScript をボスに付ける。どちらも StageInitializer が拾って繋ぐ。
    /// ボスの体力は StageInitializer が作って配線した TargetHealthService を受け取り、BossBattleService が段階に合わせて切り替える。
    /// </summary>
    public sealed class BossInitializer : InitializerBase, IInjectable<IPlayerHealthController>,
        IInjectable<IPlayerMovementController>, IInjectable<ITargetHealthController>
    {
        [SerializeField]
        [Tooltip("切り離されたボクセルの物を引き寄せる吸い寄せ。同じシーンにあるので Inspector で渡す。なければ引き寄せない")]
        private VoxelAttraction _voxelAttraction;

        [SerializeField]
        [Tooltip("頭部のレーザーで敵を吸収・撃破する先。なければ敵を巻き込まない")]
        private EnemySpawnAdapter _enemySpawnAdapter;

        [SerializeField, Min(1)]
        [Tooltip("ボス戦の足踏みの段階で足踏みする回数")]
        private int _stompsPerRound = 3;

        [SerializeField, Min(1f)]
        [Tooltip("最終の段階で、ボスの体力が満タンから尽きるまでの時間（秒）")]
        private float _finalDuration = 20f;

        [SerializeField]
        [Tooltip("ボス戦の行動の回数・間隔・待機")]
        private BossBattleSettings _battleSettings = new();

        [SerializeField]
        [Tooltip("ボス戦の攻撃が与えるダメージ量")]
        private BossDamageSettings _damageSettings = new();

        [SerializeField]
        [Tooltip("初期化の終わりにボス戦を始める")]
        private bool _beginOnInitialize = true;

        private IPlayerHealthController _healthService;
        private IPlayerMovementController _movementService;
        private ITargetHealthController _targetHealthService;

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
                shockwaves.Initialize(applyDamage, _damageSettings.Shockwave,
                    _movementService != null ? _movementService.RequestKnockback : null);
            }

            var finisherStrikes = boss.GetComponentInChildren<BossFinisherStrikes>(true);
            if (finisherStrikes != null)
            {
                finisherStrikes.SetTarget(target);
                finisherStrikes.Initialize(applyDamage, _damageSettings.FinisherStrike);
            }

            var laser = boss.GetComponentInChildren<BossLaser>(true);
            if (laser != null)
            {
                laser.SetTarget(target);
                laser.Initialize(applyDamage, _damageSettings.Laser);
            }

            var turret = boss.GetComponentInChildren<BossTurret>(true);
            if (turret != null)
            {
                turret.SetTarget(target);
                turret.Initialize(applyDamage, _damageSettings.Turret);
            }

            var headLaser = boss.GetComponentInChildren<BossHeadLaser>(true);
            if (headLaser != null)
            {
                headLaser.SetTarget(target);
                headLaser.Initialize(applyDamage, _damageSettings.HeadLaser,
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
                arena.Initialize(applyDamage, _damageSettings.ArenaOutside, _damageSettings.ArenaBeam);
            }

            boss.AddComponent<BossTowerThrow>().Initialize(target, applyDamage, _damageSettings.TowerToPlayer,
                _damageSettings.TowerToBoss, arena);

            var battleService = new BossBattleService(blackBoard, _targetHealthService, _battleSettings, _stompsPerRound,
                _finalDuration, gameObject.scene.buildIndex);
            if (blackBoard.TryGetSceneState<EnemyBoard, IBossBattleState>(out var battleState, this))
            {
                battleAdapter.Initialize(blackBoard, battleState, battleService.ReportCrystalsCleared,
                    battleService.ReportActionFinished, battleService.Tick, target, _voxelAttraction);
                if (boss.TryGetComponent<HealthTarget>(out var healthTarget))
                {
                    healthTarget.SetOnPlayerHit(battleService.ReportPlayerHit);
                }

                boss.AddComponent<BossTargetSelector>().Connect(battleState, boss.GetComponentInChildren<BossCrystals>(true));
                boss.AddComponent<BossGuideScript>();
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

        public void Inject(IPlayerHealthController instance)
        {
            _healthService = instance;
        }

        public void Inject(IPlayerMovementController instance)
        {
            _movementService = instance;
        }

        public void Inject(ITargetHealthController instance)
        {
            _targetHealthService = instance;
        }
    }
}
