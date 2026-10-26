using System;
using System.Text;
using Kizami.BlackBoard;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボス戦で、BossBattleState の段階に合わせてボスの見た目と武装を切り替え、BossBattleService が決めた行動を 1 つずつ実行して、終わったら報告する。ボスのボクセルのモデル（VoxelModelLoader）と同じ GameObject に付ける。
    /// 1 周目の飛来は、プレイヤーの正面の着地点へ降りてくる。
    /// 水晶の段階は、退く所へ飛び去ってから水晶を投げて置き、上部タレット・降下フィニッシャー・頭部のレーザーで遠くから攻撃する。水晶がすべて壊れたら、飛来の段階で水晶のあった所へ飛ぶ。
    /// ダウンの段階はコアを出したまま壊せる武装を赤く塗り、武装を壊す行動で、BossWeapons が選んだ武装を 1 つ壊す。
    /// 止まったあとは攻撃をやめ、出ている降下フィニッシャーと塔も消す。
    /// 武装の種類ごとの数と、壊せる武装が残っているかを BossWeaponState に書き、行えない行動を飛ばす判断は BossBattleService に任せる。
    /// </summary>
    /// <remarks>
    /// ボス戦の間、ボスは自分では歩かない（飛来でだけ動く）。武装を含むすべてのパーツを止まるまで保護し、削れないようにする。
    /// 体力の配線は StageInitializer が行う。ボスの HealthTarget は、体力が尽きても保護を外さない設定にする。
    /// 上部タレットは水晶の段階でだけ使う。円形の空間は足踏みの段階に入るときに作り、水晶の段階に入るときと止まったときに閉じる。
    /// 水晶・飛来・上部タレット・頭部のレーザーは無くてもよく、無ければその攻撃をせず、水晶と飛来は済んだものとして進める。
    /// 始まってから止まるまで、胴体が吸い寄せの優先の引き寄せ対象になる。
    /// </remarks>
    [RequireComponent(typeof(VoxelModelLoader))]
    public sealed class BossBattleAdapter : MonoBehaviour, IAttractionTarget
    {
        [SerializeField]
        [Tooltip("ボスの胴体")]
        private BossBody _body;

        [SerializeField]
        [Tooltip("ボスの武装")]
        private BossWeapons _weapons;

        [SerializeField]
        [Tooltip("ボスのアウトリガーの衝撃波")]
        private BossShockwaves _shockwaves;

        [SerializeField]
        [Tooltip("ボスの下部レーザー")]
        private BossLaser _laser;

        [SerializeField]
        [Tooltip("ボスの脚の Hatch が呼ぶ降下フィニッシャー")]
        private BossFinisherStrikes _finisherStrikes;

        [SerializeField]
        [Tooltip("ボスの上部タレット。水晶の段階で使う")]
        private BossTurret _turret;

        [SerializeField]
        [Tooltip("ボスの頭部のレーザー。水晶の段階で使う")]
        private BossHeadLaser _headLaser;

        [SerializeField]
        [Tooltip("水晶の段階で投げる水晶")]
        private BossCrystals _crystals;

        [SerializeField]
        [Tooltip("水晶のあった所へ飛んでくる飛来")]
        private BossArrival _arrival;

        [SerializeField]
        [Tooltip("プレイヤーを閉じ込める円形の空間。なければ閉じ込めない")]
        private BossArena _arena;

        [SerializeField]
        [Tooltip("1 周目の飛来で降りる所。なければ今の位置に降りる")]
        private Transform _openingLandingPoint;

        [SerializeField]
        [Tooltip("水晶の段階の初めに飛び去る先。なければその場で水晶を投げる")]
        private Transform _retreatPoint;

        [SerializeField, Min(0.1f)]
        [Tooltip("水晶の段階で、降下フィニッシャーを撃つ間隔（秒）。直接戦闘より長くして、遠距離の圧を下げる")]
        private float _crystalStrikeCooldown = 6f;

        [SerializeField, Min(0f)]
        [Tooltip("飛来の間に建物を前もってボクセルにする円の、円形の空間の半径に足す幅（m）。柵のビームが溶かす穴の半径より大きくする")]
        private float _preloadMargin = 8f;

        private readonly BossWeaponState _weaponState = new();

        private VoxelModelLoader _loader;
        private IBossBattleState _state;
        private Action _reportCrystalsCleared;
        private Action<bool> _reportActionFinished;
        private Action<float> _tick;
        private VoxelAttraction _attraction;
        private HealthTarget _healthTarget;
        private BossTowerThrow _towerThrow;
        private Transform _target;
        private BossBattlePhase _handledPhase = BossBattlePhase.Idle;
        private int _handledActionCount;

        /// <summary> 今の行動を終えたことを報告したか </summary>
        private bool _isActionReported;

        /// <summary> 下部レーザーを撃つ行動で、撃つ番を進めたか </summary>
        private bool _isLaserStarted;

        /// <summary> すべてのパーツを保護したか </summary>
        private bool _isProtected;

        /// <summary>
        /// ボス戦の State と、段階が進む出来事を報告する関数を受け取り、BossWeaponState を登録する。
        /// </summary>
        /// <param name="blackBoard">BossWeaponState の登録先</param>
        /// <param name="state">ボス戦の State</param>
        /// <param name="reportCrystalsCleared">水晶をすべて壊したことを報告する関数（BossBattleService.ReportCrystalsCleared）</param>
        /// <param name="reportActionFinished">今の行動を終えたことを報告する関数（BossBattleService.ReportActionFinished）。引数は行動を実際に行えたか</param>
        /// <param name="tick">毎フレーム経過時間を渡す関数（BossBattleService.Tick）。体力の無敵と落ちる塊を数えるかを決め、待機の時間を数え、体力が尽きたかを調べ、最終の体力を減らす</param>
        /// <param name="target">プレイヤー。ダウンで壊す武装を選ぶのに使う</param>
        /// <param name="attraction">胴体を優先の引き寄せ対象として登録する吸い寄せ。なければ登録しない</param>
        public void Initialize(IBlackBoard blackBoard, IBossBattleState state, Action reportCrystalsCleared,
            Action<bool> reportActionFinished, Action<float> tick, Transform target, VoxelAttraction attraction)
        {
            _state = state;
            _attraction = attraction;
            if (_weapons != null)
            {
                _weaponState.SetCounts(_weapons.CountOf(BossWeaponKind.Outrigger), _weapons.CountOf(BossWeaponKind.Laser),
                    _weapons.CountOf(BossWeaponKind.Missile));
                _weaponState.SetHasBreakable(_weapons.CountBroken() < _weapons.Count);
            }

            if (blackBoard.TryGetBoard<EnemyBoard>(out var enemyBoard, this))
            {
                enemyBoard.RegisterSceneState<IBossWeaponState>(_weaponState, gameObject.scene.buildIndex);
            }

            _reportCrystalsCleared = reportCrystalsCleared;
            _reportActionFinished = reportActionFinished;
            _tick = tick;
            _target = target;
            _towerThrow = GetComponent<BossTowerThrow>();

            // 1 周目の着地点は初めから決まっている為、ステージの読み込み中に先読みを済ませる
            PreloadArrivalArea();
            VoxelBuilding.LoadPreloadAreaNow();
            if (_attraction != null) _attraction.AddPriorityTarget(this);
        }

        /// <summary>
        /// 段階・周・足踏みの回数と、水晶の壊れ具合を書き足す。
        /// </summary>
        public void AppendStatus(StringBuilder builder)
        {
            if (_state == null)
            {
                builder.Append("-");
                return;
            }

            builder.Append(_state.Phase).Append(" round ").Append(_state.Round)
                .Append(" stomp ").Append(_state.StompCount);
            if (_state.Phase != BossBattlePhase.Crystal || _crystals == null) return;

            builder.Append(" | ");
            _crystals.AppendStatus(builder);
        }

        private void Awake()
        {
            _loader = GetComponent<VoxelModelLoader>();
            _healthTarget = GetComponent<HealthTarget>();
            if (_healthTarget == null)
            {
                UsefulLogger.LogWarning("ボスに HealthTarget がない為、ダウンせず、武装も壊せません。", this);
            }

            if (_body == null || _weapons == null || _shockwaves == null || _laser == null || _finisherStrikes == null)
            {
                UsefulLogger.LogError("ボスの胴体・武装・衝撃波・下部レーザー・降下フィニッシャーのどれかが設定されていない為、直接戦闘は動きません。", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            VoxelBuilding.ClearPreloadArea();
            if (_attraction != null) _attraction.RemovePriorityTarget(this);
        }

        private void Update()
        {
            if (_state == null) return;

            UpdateProtection();
            _weaponState.SetHasBreakable(_weapons.CountBroken() < _weapons.Count);
            _tick?.Invoke(Time.deltaTime);

            var phase = _state.Phase;
            if (phase != _handledPhase)
            {
                _handledPhase = phase;
                Enter(phase);
            }

            if (_state.ActionCount != _handledActionCount)
            {
                _handledActionCount = _state.ActionCount;
                BeginAction(_state.Action);
                return;
            }

            TickAction(_state.Action);
        }

        /// <summary>
        /// 段階に入ったときに一度だけ行う切り替え。
        /// </summary>
        private void Enter(BossBattlePhase phase)
        {
            switch (phase)
            {
                case BossBattlePhase.Crystal:
                    _body.SetMoving(false);
                    _laser.HoldCoreExposed(false);
                    if (_arena != null) _arena.Close();
                    if (_healthTarget != null) _healthTarget.ResetDepleted();
                    if (_towerThrow != null) _towerThrow.Clear();
                    break;
                case BossBattlePhase.Arriving:
                    if (_headLaser != null) _headLaser.Cancel();
                    PreloadArrivalArea();
                    break;
                case BossBattlePhase.Stomping:
                    _body.SetMoving(false);
                    _laser.HoldCoreExposed(false);
                    if (_arena != null && !_arena.IsOpen) _arena.Open(_body.transform.position);
                    break;
                case BossBattlePhase.Down:
                    if (_towerThrow != null) _towerThrow.Clear();
                    _laser.Cancel();
                    _laser.HoldCoreExposed(true);
                    _weapons.SetHighlight(true);
                    break;
                case BossBattlePhase.Final:
                    _laser.HoldCoreExposed(false);
                    if (_healthTarget != null) _healthTarget.ResetDepleted();
                    if (_towerThrow != null) _towerThrow.Clear();
                    break;
                case BossBattlePhase.Stopped:
                    VoxelBuilding.ClearPreloadArea();
                    if (_towerThrow != null) _towerThrow.Clear();
                    _body.SetMoving(false);
                    _weapons.SetHighlight(false);
                    _finisherStrikes.StopAll();
                    _laser.Cancel();
                    _laser.HoldCoreExposed(true);
                    if (_headLaser != null) _headLaser.Cancel();
                    if (_arena != null) _arena.Close();
                    break;
            }
        }

        /// <summary>
        /// 行動を始める。飛ぶ行動は飛び始め、それ以外は次のフレームの TickAction から実行する。
        /// </summary>
        private void BeginAction(BossAction action)
        {
            _isActionReported = false;
            _isLaserStarted = false;
            switch (action)
            {
                case BossAction.Retreat:
                    if (_arrival != null && _retreatPoint != null) _arrival.Begin(_retreatPoint.position);
                    break;
                case BossAction.Arrive:
                    if (_arrival != null) _arrival.Begin(GetArrivalDestination());
                    break;
            }
        }

        /// <summary>
        /// 今の行動を 1 フレームぶん進め、終えたら報告する。待機・攻撃を待つ・何もしない行動では何もしない。
        /// </summary>
        private void TickAction(BossAction action)
        {
            if (_isActionReported) return;

            switch (action)
            {
                case BossAction.Retreat:
                case BossAction.Arrive:
                    if (_arrival == null || !_arrival.IsArriving) ReportActionFinished(true);
                    break;
                case BossAction.ThrowCrystals:
                    if (_crystals != null) _crystals.Throw();
                    ReportActionFinished(true);
                    break;
                case BossAction.Barrage:
                    TickBarrage();
                    break;
                case BossAction.Stomp:
                    if (_shockwaves.TryStomp()) ReportActionFinished(true);
                    break;
                case BossAction.Laser:
                    TickLaser();
                    break;
                case BossAction.Strike:
                    if (_finisherStrikes.TryFire(0f)) ReportActionFinished(true);
                    break;
                case BossAction.AwaitStrikes:
                    if (!_finisherStrikes.IsBusy) ReportActionFinished(true);
                    break;
                case BossAction.TowerThrow:
                    ReportActionFinished(_towerThrow != null && _towerThrow.Throw());
                    break;
                case BossAction.FinalVolley:
                    TickFinalVolley();
                    break;
                case BossAction.BreakWeapon:
                    ReportActionFinished(TryBreakWeapon());
                    break;
            }
        }

        /// <summary>
        /// 上部タレット・降下フィニッシャー・頭部のレーザーを、それぞれのクールタイムごとに撃つ。水晶がすべて壊れたら報告する。
        /// </summary>
        private void TickBarrage()
        {
            if (_crystals == null || _crystals.IsCleared)
            {
                _isActionReported = true;
                _reportCrystalsCleared?.Invoke();
                return;
            }

            if (_turret != null) _turret.TryFire();
            _finisherStrikes.TryFire(_crystalStrikeCooldown);
            if (_headLaser != null) _headLaser.TryFire();
        }

        /// <summary>
        /// 次の番の下部レーザーを撃ち、撃ち終えたら報告する。番のレーザーが壊れていれば、撃たずにクールタイムだけ過ぎるので、撃ち終えたものとする。
        /// </summary>
        private void TickLaser()
        {
            if (!_isLaserStarted)
            {
                _isLaserStarted = _laser.TryFire();
                return;
            }

            if (!_laser.IsFiring) ReportActionFinished(true);
        }

        /// <summary>
        /// 下部レーザーを撃ち、撃っている間は降下フィニッシャーも撃つ。撃ち終えたら報告する。
        /// </summary>
        private void TickFinalVolley()
        {
            if (!_isLaserStarted)
            {
                _isLaserStarted = _laser.TryFire();
            }

            if (_laser.IsFiring)
            {
                _finisherStrikes.TryFire();
                return;
            }

            if (_isLaserStarted) ReportActionFinished(true);
        }

        private void ReportActionFinished(bool isPerformed)
        {
            _isActionReported = true;
            _reportActionFinished?.Invoke(isPerformed);
        }

        /// <summary>
        /// 飛来の着く所。1 周目はプレイヤーの正面の着地点、2 周目からは水晶を置いた場所の中心。
        /// </summary>
        private Vector3 GetArrivalDestination()
        {
            if (_state.Round <= 1) return _openingLandingPoint != null ? _openingLandingPoint.position : transform.position;

            return _crystals != null ? _crystals.PlacedCenter : transform.position;
        }

        /// <summary>
        /// 飛来の着く所の周りの建物を、前もってボクセルにさせる。着いて柵を張ると周りの建物がまとめて崩れ、崩れる階を 1 フレームでボクセルにする負荷を散らす為。
        /// </summary>
        private void PreloadArrivalArea()
        {
            if (_arena != null) VoxelBuilding.SetPreloadArea(GetArrivalDestination(), _arena.Radius + _preloadMargin);
        }

        /// <summary>
        /// 画面の中央に近い武装を 1 つ壊し、残りの赤を消す。カメラがないか、壊せる武装がなければ壊さずに false を返す。
        /// </summary>
        private bool TryBreakWeapon()
        {
            var view = Camera.main;
            if (view == null) return false;

            var viewTransform = view.transform;
            var playerPosition = _target != null ? _target.position : viewTransform.position;
            if (_weapons.BreakBest(viewTransform.position, viewTransform.forward, playerPosition) < 0) return false;

            _weapons.SetHighlight(false);
            return true;
        }

        /// <summary>
        /// モデルを読み込んだら、武装を含むすべてのパーツを保護する。止まったら保護を外す。
        /// </summary>
        private void UpdateProtection()
        {
            var shouldProtect = !_state.IsStopped;
            if (!_loader.IsLoaded || _isProtected == shouldProtect) return;

            foreach (var part in _loader.Parts)
            {
                part.SetProtected(shouldProtect);
            }

            _isProtected = shouldProtect;
        }

        public bool TryGetAttractionPoint(out Vector3 point)
        {
            point = _body != null ? _body.transform.position : transform.position;
            return _state != null && _state.Phase != BossBattlePhase.Idle && _state.Phase != BossBattlePhase.Stopped;
        }
    }
}
