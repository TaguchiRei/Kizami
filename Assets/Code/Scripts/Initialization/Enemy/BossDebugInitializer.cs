using System.Text;
using Kizami.EngineAdapter;
using UnityEngine;
using UnityEngine.InputSystem;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Debugging;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// ボスの武装の状態の画面表示と、武装を順番に使わせる操作（6：タレット、7：レーザー、8：ミサイル、9：アウトリガー。それぞれの武装の攻撃のコンポーネントがあれば撃たせる）を行うデバッグ用の Initializer。インゲームのシーンへ置き、ボスはステージシーンから探す。
    /// 操作はデバッグ専用の為、入力マップに Action を足さず Keyboard.current を直接読む。
    /// 表示には DebugGUI がシーンに必要（UsefulToolkit/ProgramTools/DebugGUI Setup）。エディタと Development Build でのみ動く。
    /// </summary>
    public sealed class BossDebugInitializer : InitializerBase
    {
        private readonly StringBuilder _weaponText = new();
        private readonly StringBuilder _battleText = new();

        /// <summary> ボスの武装。ボスのいないステージでは null </summary>
        private BossWeapons _weapons;

        /// <summary> ボスの直接戦闘。段階を画面に出す </summary>
        private BossBattleAdapter _battle;

        /// <summary> ボスの上部タレット。6 キーで撃たせる </summary>
        private BossTurret _turret;

        /// <summary> ボスの下部レーザー。7 キーで撃たせる </summary>
        private BossLaser _laser;

        /// <summary> ボスの脚のミサイル。8 キーで撃たせる </summary>
        private BossMissiles _missiles;

        /// <summary> ボスのアウトリガーの衝撃波。9 キーで足踏みさせる </summary>
        private BossShockwaves _shockwaves;

        public override void Initialize(IBlackBoard blackBoard)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            FindBoss();
            if (_weapons != null)
            {
                DebugGUI.ObserveVariable("Boss Weapons", GetWeaponText, this);
            }

            if (_battle != null)
            {
                DebugGUI.ObserveVariable("Boss Battle", GetBattleText, this);
            }
#endif
            base.Initialize(blackBoard);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// ステージシーンに置いたボスから、武装と攻撃のコンポーネントを集める。
        /// </summary>
        private void FindBoss()
        {
            _weapons = FindFirstObjectByType<BossWeapons>();
            if (_weapons == null) return;

            var boss = _weapons.gameObject;
            _battle = boss.GetComponentInChildren<BossBattleAdapter>(true);
            _turret = boss.GetComponentInChildren<BossTurret>(true);
            _laser = boss.GetComponentInChildren<BossLaser>(true);
            _missiles = boss.GetComponentInChildren<BossMissiles>(true);
            _shockwaves = boss.GetComponentInChildren<BossShockwaves>(true);
        }

        private void Update()
        {
            if (_weapons == null) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit6Key.wasPressedThisFrame)
            {
                if (_turret != null) _turret.TryFire();
                else Use(BossWeaponKind.Turret);
            }

            if (keyboard.digit7Key.wasPressedThisFrame)
            {
                if (_laser != null) _laser.TryFire();
                else Use(BossWeaponKind.Laser);
            }
            if (keyboard.digit8Key.wasPressedThisFrame)
            {
                if (_missiles != null) _missiles.TryFire();
                else Use(BossWeaponKind.Missile);
            }
            if (keyboard.digit9Key.wasPressedThisFrame)
            {
                if (_shockwaves != null) _shockwaves.TryStomp();
                else Use(BossWeaponKind.Outrigger);
            }
        }

        /// <summary>
        /// 種類 kind の次の武装の番を進め、使った武装と、壊れていて撃たなかったかをログに出す。
        /// </summary>
        private void Use(BossWeaponKind kind)
        {
            var index = _weapons.TakeNext(kind);
            if (index < 0)
            {
                UsefulLogger.LogWarning($"ボスに {kind} の武装がありません。", this);
                return;
            }

            UsefulLogger.Log(_weapons.IsBroken(index)
                ? $"{kind}{index} は壊れている為、撃たずにクールタイムだけを発生させます。"
                : $"{kind}{index} を使います。", this);
        }

        private string GetBattleText()
        {
            _battleText.Clear();
            _battle.AppendStatus(_battleText);
            return _battleText.ToString();
        }

        private string GetWeaponText()
        {
            _weaponText.Clear();
            _weapons.AppendStatus(_weaponText);
            return _weaponText.ToString();
        }
#endif
    }
}
