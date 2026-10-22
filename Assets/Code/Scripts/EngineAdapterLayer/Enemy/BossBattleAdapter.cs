using System;
using System.Text;
using Kizami.BlackBoard;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボス戦で、BossBattleState の段階に合わせてボスの武装を動かし、段階が進む出来事を報告する。ボスのボクセルのモデル（VoxelModelLoader）と同じ GameObject に付ける。
    /// 1 周目の飛来は、プレイヤーの正面の着地点へ降りてくる。
    /// 水晶の段階は、退く所へ飛び去ってから水晶を投げて置き、上部タレット・降下フィニッシャー・頭部のレーザーで遠くから攻撃する。水晶がすべて壊れたら、飛来の段階で水晶のあった所へ飛ぶ。
    /// 直接戦闘は一度に1つの行動だけを行う。足踏みの段階はアウトリガーで足踏みし、コアを出す段階は下部レーザー → 降下フィニッシャー → 塔投げ → 待機の順に、前の行動が終わってから次へ進む。
    /// ダウンの段階はコアを出したまま壊せる武装を赤く塗り、ボスに攻撃が当たったら（HealthTarget が知らせる）、BossWeapons が選んだ武装を 1 つ壊す。
    /// 最終の段階は、足踏みしてから下部レーザーと降下フィニッシャーを同時に撃つ流れを、止まるまで繰り返す（武装を同時に使うのは最終だけ）。止まったあとは攻撃をやめ、出ている降下フィニッシャーと塔も消す。
    /// </summary>
    /// <remarks>
    /// ボス戦の間、ボスは自分では歩かない（飛来でだけ動く）。武装を含むすべてのパーツを止まるまで保護し、削れないようにする。
    /// 体力の配線は StageInitializer が行う。ボスの HealthTarget は、体力が尽きても保護を外さない設定にする。
    /// 上部タレットは水晶の段階でだけ使う。円形の空間は足踏みの段階に入るときに作り、水晶の段階に入るときと止まったときに閉じる。
    /// 水晶・飛来・上部タレット・頭部のレーザーは無くてもよく、無ければその攻撃をせず、水晶と飛来は済んだものとして進める。
    /// </remarks>
    [RequireComponent(typeof(VoxelModelLoader))]
    public sealed class BossBattleAdapter : MonoBehaviour
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

        [SerializeField, Min(0f)]
        [Tooltip("飛来で着いてから、足踏みを始めるまでの時間（秒）。脚が足を置き直す間は攻撃しない")]
        private float _landingPause = 1f;

        [SerializeField, Min(0.1f)]
        [Tooltip("水晶の段階で、降下フィニッシャーを撃つ間隔（秒）。直接戦闘より長くして、遠距離の圧を下げる")]
        private float _crystalStrikeCooldown = 6f;

        [SerializeField, Min(1)]
        [Tooltip("直接戦闘のコア露出の行動で、降下フィニッシャーを続けて出す数")]
        private int _meleeStrikeCount = 3;

        [SerializeField, Min(0.1f)]
        [Tooltip("直接戦闘のコア露出の行動で、降下フィニッシャーを出す間隔（秒）")]
        private float _meleeStrikeInterval = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("塔を投げてから、次の行動に移るまでの待機（秒）。この間は何もしない")]
        private float _towerRest = 10f;

        [SerializeField, Min(0f)]
        [Tooltip("足踏みを始めてから、落ちてくる塊のダメージを数え始めるまでの猶予（秒）。飛来で崩れた塊が落ちきるのを待つ")]
        private float _collapseGrace = 2f;

        [SerializeField, Min(1)]
        [Tooltip("最終の段階で、コアを出す攻撃の前に足踏みする回数")]
        private int _finalStomps = 3;

        private VoxelModelLoader _loader;
        private IBossBattleState _state;
        private Action _reportCrystalsCleared;
        private Action _reportArrived;
        private Action _reportStomp;
        private Action _reportCoreAttackFinished;
        private Action _reportWeaponBroken;
        private Action<float> _tick;
        private HealthTarget _healthTarget;
        private BossTowerThrow _towerThrow;
        private Transform _target;
        private BossBattlePhase _handledPhase = BossBattlePhase.Idle;

        /// <summary> ダウンに入ったときに壊れていた武装の数。これを超えたら、武装が 1 つ壊れたとする </summary>
        private int _brokenBaseline;

        /// <summary> コアを出す段階で、下部レーザーの番を進めたか </summary>
        private bool _isLaserStarted;

        /// <summary> 今のダウンで、武装を壊したか </summary>
        private bool _isWeaponBrokenInDown;

        /// <summary> 直接戦闘のコア露出で今行っている行動 </summary>
        private MeleeAction _meleeAction;

        /// <summary> 降下フィニッシャーの行動で、あと何回出すか </summary>
        private int _strikesRemaining;

        /// <summary> 次の降下フィニッシャーを出すまでの時間（秒） </summary>
        private float _strikeTimer;

        /// <summary> 塔を投げたあとの待機の残り時間（秒） </summary>
        private float _restRemaining;

        /// <summary> 最終の段階で、今の周に足踏みした回数 </summary>
        private int _finalStompCount;

        /// <summary> 足踏みとコア露出の段階が続いている時間（秒）。それ以外の段階では0 </summary>
        private float _fightTime;

        /// <summary> すべてのパーツを保護したか </summary>
        private bool _isProtected;

        /// <summary> 今の水晶の段階で、水晶を投げたか </summary>
        private bool _isCrystalThrown;

        /// <summary> 飛来で着いてからの時間（秒） </summary>
        private float _landedTime;

        /// <summary>
        /// ボス戦の State と、段階が進む出来事を報告する関数を受け取る。
        /// </summary>
        /// <param name="state">ボス戦の State</param>
        /// <param name="reportCrystalsCleared">水晶をすべて壊したことを報告する関数（BossBattleService.ReportCrystalsCleared）</param>
        /// <param name="reportArrived">飛来を終えたことを報告する関数（BossBattleService.ReportArrived）</param>
        /// <param name="reportStomp">足踏みしたことを報告する関数（BossBattleService.ReportStomp）</param>
        /// <param name="reportCoreAttackFinished">コアを出した攻撃を終えたことを報告する関数（BossBattleService.ReportCoreAttackFinished）</param>
        /// <param name="reportWeaponBroken">武装が 1 つ壊れたことを報告する関数（BossBattleService.ReportWeaponBroken）</param>
        /// <param name="tick">毎フレーム経過時間を渡す関数（BossBattleService.Tick）。体力が尽きたかを調べ、最終の体力を減らす</param>
        /// <param name="target">プレイヤー。ダウンで壊す武装を選ぶのに使う</param>
        public void Initialize(IBossBattleState state, Action reportCrystalsCleared, Action reportArrived,
            Action reportStomp, Action reportCoreAttackFinished, Action reportWeaponBroken, Action<float> tick,
            Transform target)
        {
            _state = state;
            _reportCrystalsCleared = reportCrystalsCleared;
            _reportArrived = reportArrived;
            _reportStomp = reportStomp;
            _reportCoreAttackFinished = reportCoreAttackFinished;
            _reportWeaponBroken = reportWeaponBroken;
            _tick = tick;
            _target = target;
            _towerThrow = GetComponent<BossTowerThrow>();
            if (_healthTarget != null) _healthTarget.SetOnHitWhileDepleted(OnHitWhileDepleted);
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

        private void Update()
        {
            if (_state == null) return;

            UpdateProtection();
            UpdateCollapseCounting(Time.deltaTime);
            _tick?.Invoke(Time.deltaTime);

            var phase = _state.Phase;
            if (phase != _handledPhase)
            {
                _handledPhase = phase;
                Enter(phase);
                return;
            }

            switch (phase)
            {
                case BossBattlePhase.Crystal:
                    TickCrystal();
                    break;
                case BossBattlePhase.Arriving:
                    TickArriving(Time.deltaTime);
                    break;
                case BossBattlePhase.Stomping:
                    TickStomping();
                    break;
                case BossBattlePhase.CoreExposed:
                    TickCoreExposed();
                    break;
                case BossBattlePhase.Down:
                    TickDown();
                    break;
                case BossBattlePhase.Final:
                    TickFinal();
                    break;
            }
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
                    _isCrystalThrown = false;
                    if (_towerThrow != null) _towerThrow.Clear();
                    if (_arrival != null && _retreatPoint != null) _arrival.Begin(_retreatPoint.position);
                    break;
                case BossBattlePhase.Arriving:
                    if (_headLaser != null) _headLaser.Cancel();
                    _landedTime = 0f;
                    if (_arrival != null) _arrival.Begin(GetArrivalDestination());
                    break;
                case BossBattlePhase.Stomping:
                    _body.SetMoving(false);
                    _laser.HoldCoreExposed(false);
                    if (_arena != null && !_arena.IsOpen) _arena.Open(_body.transform.position);
                    break;
                case BossBattlePhase.CoreExposed:
                    _isLaserStarted = false;
                    _meleeAction = MeleeAction.Laser;
                    break;
                case BossBattlePhase.Down:
                    if (_towerThrow != null) _towerThrow.Clear();
                    _laser.Cancel();
                    _laser.HoldCoreExposed(true);
                    _brokenBaseline = _weapons.CountBroken();
                    _isWeaponBrokenInDown = false;
                    _weapons.SetHighlight(true);
                    break;
                case BossBattlePhase.Final:
                    _laser.HoldCoreExposed(false);
                    if (_healthTarget != null) _healthTarget.ResetDepleted();
                    if (_towerThrow != null) _towerThrow.Clear();
                    _finalStompCount = 0;
                    _isLaserStarted = false;
                    break;
                case BossBattlePhase.Stopped:
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
        /// 飛び去り終えたら水晶を投げ、上部タレット・降下フィニッシャー・頭部のレーザーを、それぞれのクールタイムごとに撃つ。水晶がすべて壊れたら報告する。
        /// </summary>
        private void TickCrystal()
        {
            if (_arrival != null && _arrival.IsArriving) return;

            if (!_isCrystalThrown)
            {
                _isCrystalThrown = true;
                if (_crystals != null) _crystals.Throw();
                return;
            }

            if (_crystals == null || _crystals.IsCleared)
            {
                _reportCrystalsCleared?.Invoke();
                return;
            }

            if (_turret != null) _turret.TryFire();
            _finisherStrikes.TryFire(_crystalStrikeCooldown);
            if (_headLaser != null) _headLaser.TryFire();
        }

        /// <summary>
        /// 着くまで待ち、着いてから決めた時間が過ぎたら報告する。飛んでいる間と着いた直後は攻撃しない。
        /// </summary>
        private void TickArriving(float deltaTime)
        {
            if (_arrival != null && _arrival.IsArriving) return;

            _landedTime += deltaTime;
            if (_landedTime >= _landingPause) _reportArrived?.Invoke();
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
        /// アウトリガーで足踏みし、足踏みしたら報告する。アウトリガーが 1 つもなければ、足踏みしたことにして進める。
        /// </summary>
        private void TickStomping()
        {
            if (_weapons.CountOf(BossWeaponKind.Outrigger) == 0 || _shockwaves.TryStomp())
            {
                _reportStomp?.Invoke();
            }
        }

        /// <summary>
        /// コアを出している間、下部レーザー → 降下フィニッシャー → 塔投げと待機の順に、一度に1つずつ行う。前の行動が終わるまで次を始めない。
        /// 番のレーザーが壊れていれば、撃たずにクールタイムだけ過ぎるので、撃ち終えたものとして次へ進む。待機を終えたら報告する。
        /// </summary>
        private void TickCoreExposed()
        {
            switch (_meleeAction)
            {
                case MeleeAction.Laser:
                    if (!_isLaserStarted)
                    {
                        _isLaserStarted = _weapons.CountOf(BossWeaponKind.Laser) == 0 || _laser.TryFire();
                        return;
                    }

                    if (_laser.IsFiring) return;

                    _strikesRemaining = _weapons.CountOf(BossWeaponKind.Missile) == 0 ? 0 : _meleeStrikeCount;
                    _meleeAction = MeleeAction.Strikes;
                    break;
                case MeleeAction.Strikes:
                    TickStrikes();
                    break;
                case MeleeAction.Rest:
                    _restRemaining -= Time.deltaTime;
                    if (_restRemaining <= 0f) _reportCoreAttackFinished?.Invoke();
                    break;
            }
        }

        /// <summary>
        /// 降下フィニッシャーを決めた数だけ間隔を空けて出し、出したビームがすべて終わったら塔投げに移る。
        /// </summary>
        private void TickStrikes()
        {
            if (_strikesRemaining > 0)
            {
                _strikeTimer -= Time.deltaTime;
                if (_strikeTimer > 0f || !_finisherStrikes.TryFire(_meleeStrikeInterval)) return;

                _strikesRemaining--;
                _strikeTimer = _meleeStrikeInterval;
                return;
            }

            if (_finisherStrikes.IsBusy) return;

            _restRemaining = _towerThrow != null && _towerThrow.Throw() ? _towerRest : 0f;
            _meleeAction = MeleeAction.Rest;
        }

        /// <summary>
        /// 壊れた武装の数が、ダウンに入ったときより増えたら報告する。壊せる武装が残っていなければ、壊したものとして報告する。
        /// </summary>
        private void TickDown()
        {
            var brokenCount = _weapons.CountBroken();
            if (brokenCount > _brokenBaseline || brokenCount >= _weapons.Count) _reportWeaponBroken?.Invoke();
        }

        /// <summary>
        /// 足踏みを決めた回数したあと、下部レーザーを撃ち、撃っている間は降下フィニッシャーも撃つ。撃ち終えたら足踏みから繰り返す。
        /// 終わりは BossBattleService が体力で決める。
        /// </summary>
        private void TickFinal()
        {
            if (_finalStompCount < _finalStomps)
            {
                if (_weapons.CountOf(BossWeaponKind.Outrigger) == 0 || _shockwaves.TryStomp()) _finalStompCount++;
                return;
            }

            if (!_isLaserStarted)
            {
                _isLaserStarted = _weapons.CountOf(BossWeaponKind.Laser) == 0 || _laser.TryFire();
            }

            if (_laser.IsFiring)
            {
                _finisherStrikes.TryFire();
                return;
            }

            if (!_isLaserStarted) return;

            _isLaserStarted = false;
            _finalStompCount = 0;
        }

        /// <summary>
        /// 体力が尽きたあとにボスへ攻撃が当たったら、ダウンの間に 1 回だけ、画面の中央に近い武装を 1 つ壊し、残りの赤を消す。
        /// </summary>
        private void OnHitWhileDepleted()
        {
            if (_state == null || _state.Phase != BossBattlePhase.Down || _isWeaponBrokenInDown) return;

            var view = Camera.main;
            if (view == null) return;

            var viewTransform = view.transform;
            var playerPosition = _target != null ? _target.position : viewTransform.position;
            _isWeaponBrokenInDown = _weapons.BreakBest(viewTransform.position, viewTransform.forward, playerPosition) >= 0;
            if (_isWeaponBrokenInDown) _weapons.SetHighlight(false);
        }

        /// <summary>
        /// 足踏みとコア露出の間だけ、落ちてくる塊のダメージを数える。飛来や水晶の段階の崩落、外れた武装の落下で、戦っていないのに体力が減らないようにする。
        /// 飛来の間と、着地して足踏みを始めてから猶予の時間が過ぎるまでは無敵にし、飛来の崩落の落下を待つ。
        /// </summary>
        private void UpdateCollapseCounting(float deltaTime)
        {
            if (_healthTarget == null) return;

            var phase = _state.Phase;
            if (phase != BossBattlePhase.Stomping && phase != BossBattlePhase.CoreExposed)
            {
                _fightTime = 0f;
                _healthTarget.SetCollapseCounting(false);
                _healthTarget.SetInvulnerable(phase == BossBattlePhase.Arriving);
                return;
            }

            _fightTime += deltaTime;
            var isGraceOver = _fightTime >= _collapseGrace;
            _healthTarget.SetCollapseCounting(isGraceOver);
            _healthTarget.SetInvulnerable(!isGraceOver);
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

        /// <summary>
        /// 直接戦闘のコア露出で行う行動。上から順に1つずつ行う。
        /// </summary>
        private enum MeleeAction
        {
            /// <summary> 下部レーザー </summary>
            Laser,

            /// <summary> 降下フィニッシャー </summary>
            Strikes,

            /// <summary> 塔投げのあとの待機 </summary>
            Rest
        }
    }
}
