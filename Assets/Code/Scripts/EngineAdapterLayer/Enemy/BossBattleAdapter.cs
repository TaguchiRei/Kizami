using System;
using System.Text;
using Kizami.BlackBoard;
using Kizami.EngineAdapter.Voxel;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスとの直接戦闘で、BossBattleState の段階に合わせてボスの武装を動かし、段階が進む出来事を報告する。ボスのボクセルのモデル（VoxelModelLoader）と同じ GameObject に付ける。
    /// 足踏みの段階はアウトリガーで足踏みし、コアを出す段階は下部レーザーとミサイルで攻撃する。隙の段階はコアを出したまま、武装が壊れるのを待つ。
    /// 総攻撃は、レーザーを撃ち続け、ミサイルを撃ち切り、衝撃波を消えるたびに出す。止まったあとは攻撃をやめる。
    /// </summary>
    /// <remarks>
    /// 直接戦闘の間、ボスは動かさない。武装以外のパーツは止まるまで保護し、削れないようにする。
    /// 上部タレットは直接戦闘では使わない。円形の空間は最初の足踏みで作り、止まったときに閉じる。
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
        [Tooltip("ボスの脚のミサイル")]
        private BossMissiles _missiles;

        [SerializeField]
        [Tooltip("プレイヤーを閉じ込める円形の空間。なければ閉じ込めない")]
        private BossArena _arena;

        [SerializeField, Min(0.1f)]
        [Tooltip("総攻撃を続ける時間（秒）")]
        private float _allOutDuration = 10f;

        private VoxelModelLoader _loader;
        private IBossBattleState _state;
        private Action _reportStomp;
        private Action _reportCoreAttackFinished;
        private Action _reportWeaponBroken;
        private Action _reportAllOutFinished;
        private BossBattlePhase _handledPhase = BossBattlePhase.Idle;

        /// <summary> 今の周を始めたときに壊れていた武装の数。これを超えたら、武装が 1 つ壊れたとする </summary>
        private int _brokenBaseline;

        /// <summary> コアを出す段階で、下部レーザーの番を進めたか </summary>
        private bool _isLaserStarted;

        /// <summary> 総攻撃を始めてからの時間（秒） </summary>
        private float _allOutTime;

        /// <summary> 武装以外のパーツを保護したか </summary>
        private bool _isProtected;

        /// <summary>
        /// 直接戦闘の State と、段階が進む出来事を報告する関数を受け取る。
        /// </summary>
        /// <param name="state">直接戦闘の State</param>
        /// <param name="reportStomp">足踏みしたことを報告する関数（BossBattleService.ReportStomp）</param>
        /// <param name="reportCoreAttackFinished">コアを出した攻撃を終えたことを報告する関数（BossBattleService.ReportCoreAttackFinished）</param>
        /// <param name="reportWeaponBroken">武装が 1 つ壊れたことを報告する関数（BossBattleService.ReportWeaponBroken）</param>
        /// <param name="reportAllOutFinished">総攻撃を終えたことを報告する関数（BossBattleService.ReportAllOutFinished）</param>
        public void Initialize(IBossBattleState state, Action reportStomp, Action reportCoreAttackFinished,
            Action reportWeaponBroken, Action reportAllOutFinished)
        {
            _state = state;
            _reportStomp = reportStomp;
            _reportCoreAttackFinished = reportCoreAttackFinished;
            _reportWeaponBroken = reportWeaponBroken;
            _reportAllOutFinished = reportAllOutFinished;
        }

        /// <summary>
        /// 段階・周・足踏みの回数を書き足す。
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
        }

        private void Awake()
        {
            _loader = GetComponent<VoxelModelLoader>();

            if (_body == null || _weapons == null || _shockwaves == null || _laser == null || _missiles == null)
            {
                UsefulLogger.LogError("ボスの胴体・武装・衝撃波・下部レーザー・ミサイルのどれかが設定されていない為、直接戦闘は動きません。", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (_state == null) return;

            UpdateProtection();

            var phase = _state.Phase;
            if (phase != _handledPhase)
            {
                _handledPhase = phase;
                Enter(phase);
                return;
            }

            switch (phase)
            {
                case BossBattlePhase.Stomping:
                    TickStomping();
                    break;
                case BossBattlePhase.CoreExposed:
                    TickCoreExposed();
                    break;
                case BossBattlePhase.Opening:
                    TickOpening();
                    break;
                case BossBattlePhase.AllOutAttack:
                    TickAllOutAttack(Time.deltaTime);
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
                case BossBattlePhase.Stomping:
                    _body.SetMoving(false);
                    _laser.HoldCoreExposed(false);
                    _brokenBaseline = _weapons.CountBroken();
                    if (_arena != null && !_arena.IsOpen) _arena.Open(_body.transform.position);
                    break;
                case BossBattlePhase.CoreExposed:
                    _isLaserStarted = false;
                    break;
                case BossBattlePhase.Opening:
                    _laser.HoldCoreExposed(true);
                    break;
                case BossBattlePhase.AllOutAttack:
                    _laser.HoldCoreExposed(false);
                    _laser.FireFor(_allOutDuration);
                    _missiles.FireAll();
                    _allOutTime = 0f;
                    break;
                case BossBattlePhase.Stopped:
                    _body.SetMoving(false);
                    _laser.Cancel();
                    _laser.HoldCoreExposed(true);
                    if (_arena != null) _arena.Close();
                    break;
            }
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
        /// 下部レーザーを撃ち、撃っている間はミサイルも撃つ。レーザーを撃ち終えたら報告する。
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
                _missiles.TryFire();
                return;
            }

            if (_isLaserStarted) _reportCoreAttackFinished?.Invoke();
        }

        /// <summary>
        /// 壊れた武装の数が、周を始めたときより増えたら報告する。
        /// </summary>
        private void TickOpening()
        {
            if (_weapons.CountBroken() > _brokenBaseline) _reportWeaponBroken?.Invoke();
        }

        /// <summary>
        /// 衝撃波が消えていれば新しく出し、決めた時間が過ぎたら報告する。レーザーとミサイルは始めたときに出してある。
        /// </summary>
        private void TickAllOutAttack(float deltaTime)
        {
            if (!_shockwaves.HasActiveRing) _shockwaves.ForceStomp();

            _allOutTime += deltaTime;
            if (_allOutTime >= _allOutDuration) _reportAllOutFinished?.Invoke();
        }

        /// <summary>
        /// モデルを読み込んだら、武装以外のパーツを保護する。止まったら保護を外す。
        /// </summary>
        private void UpdateProtection()
        {
            var shouldProtect = !_state.IsStopped;
            if (!_loader.IsLoaded || _isProtected == shouldProtect) return;

            foreach (var part in _loader.Parts)
            {
                if (!_weapons.IsWeaponPart(part)) part.SetProtected(shouldProtect);
            }

            _isProtected = shouldProtect;
        }
    }
}
