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
    /// ボスの武装の状態の画面表示と、武装を順番に使わせる操作（6：タレット、7：レーザー、8：ミサイル、9：アウトリガー）を行うデバッグ用の Initializer。ボスを置いたシーンへ置く。
    /// 操作はデバッグ専用の為、入力マップに Action を足さず Keyboard.current を直接読む。
    /// 表示には DebugGUI がシーンに必要（UsefulToolkit/ProgramTools/DebugGUI Setup）。エディタと Development Build でのみ動く。
    /// </summary>
    public sealed class BossDebugInitializer : InitializerBase
    {
        private readonly StringBuilder _weaponText = new();

        [SerializeField]
        [Tooltip("ボスの武装")]
        private BossWeapons _weapons;

        public override void Initialize(IBlackBoard blackBoard)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (this.IsAssigned(_weapons, nameof(BossWeapons)))
            {
                DebugGUI.ObserveVariable("Boss Weapons", GetWeaponText, this);
            }
#endif
            base.Initialize(blackBoard);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            if (_weapons == null) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit6Key.wasPressedThisFrame) Use(BossWeaponKind.Turret);
            if (keyboard.digit7Key.wasPressedThisFrame) Use(BossWeaponKind.Laser);
            if (keyboard.digit8Key.wasPressedThisFrame) Use(BossWeaponKind.Missile);
            if (keyboard.digit9Key.wasPressedThisFrame) Use(BossWeaponKind.Outrigger);
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

        private string GetWeaponText()
        {
            _weaponText.Clear();
            _weapons.AppendStatus(_weaponText);
            return _weaponText.ToString();
        }
#endif
    }
}
