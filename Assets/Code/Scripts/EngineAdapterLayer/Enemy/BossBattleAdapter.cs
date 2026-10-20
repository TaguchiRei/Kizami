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
    /// 足踏みの段階はアウトリガーで足踏みし、コアを出す段階は下部レーザーと降下フィニッシャーで攻撃する。
    /// ダウンの段階はコアを出したまま壊せる武装を赤く塗り、ボスに攻撃が当たったら（HealthTarget が知らせる）、BossWeapons が選んだ武装を 1 つ壊す。
    /// 最終の段階は、レーザーを撃ち続け、降下フィニッシャーを出し、衝撃波を消えるたびに出す。止まったあとは攻撃をやめる。
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
        [Tooltip("最終の段階で、下部レーザーを撃ち続ける時間（秒）")]
        private float _allOutDuration = 10f;

        private VoxelModelLoader _loader;
        private IBossBattleState _state;
        private Action _reportCrystalsCleared;
        private Action _reportArrived;
        private Action _reportStomp;
        private Action _reportCoreAttackFinished;
        private Action _reportWeaponBroken;
        private Action<float> _tick;
        private HealthTarget _healthTarget;
        private Transform _target;
        private BossBattlePhase _handledPhase = BossBattlePhase.Idle;

        /// <summary> ダウンに入ったときに壊れていた武装の数。これを超えたら、武装が 1 つ壊れたとする </summary>
        private int _brokenBaseline;

        /// <summary> コアを出す段階で、下部レーザーの番を進めたか </summary>
        private bool _isLaserStarted;

        /// <summary> 今のダウンで、武装を壊したか </summary>
        private bool _isWeaponBrokenInDown;

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
                    break;
                case BossBattlePhase.Down:
                    _laser.Cancel();
                    _laser.HoldCoreExposed(true);
                    _brokenBaseline = _weapons.CountBroken();
                    _isWeaponBrokenInDown = false;
                    _weapons.SetHighlight(true);
                    break;
                case BossBattlePhase.Final:
                    _laser.HoldCoreExposed(false);
                    if (_healthTarget != null) _healthTarget.ResetDepleted();
                    _laser.FireFor(_allOutDuration);
                    _finisherStrikes.FireNow();
                    break;
                case BossBattlePhase.Stopped:
                    _body.SetMoving(false);
                    _weapons.SetHighlight(false);
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
            _finisherStrikes.TryFire();
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
        /// 下部レーザーを撃ち、撃っている間は降下フィニッシャーも撃つ。レーザーを撃ち終えたら報告する。
        /// 番のレーザーが壊れていれば、撃たずにクールタイムだけ過ぎるので、撃ち終えたものとして報告する。
        /// </summary>
        private void TickCoreExposed()
        {
            if (!_isLaserStarted)
            {
                _isLaserStarted = _weapons.CountOf(BossWeaponKind.Laser) == 0 || _laser.TryFire();
            }

            if (_laser.IsFiring)
            {
                _finisherStrikes.TryFire();
                return;
            }

            if (_isLaserStarted) _reportCoreAttackFinished?.Invoke();
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
        /// 衝撃波が消えていれば新しく出す。レーザーと降下フィニッシャーは始めたときに出してある。終わりは BossBattleService が体力で決める。
        /// </summary>
        private void TickFinal()
        {
            if (!_shockwaves.HasActiveRing) _shockwaves.ForceStomp();
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
    }
}
