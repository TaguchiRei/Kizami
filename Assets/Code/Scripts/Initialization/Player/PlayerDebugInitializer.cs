using System;
using Kizami.Application;
using Kizami.BlackBoard;
using Kizami.EngineAdapter;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Debugging;
using UsefulToolkit.Initialization;
using UsefulToolkit.Utility;

namespace Kizami.Initialization
{
    /// <summary>
    /// プレイヤーへダメージを与える操作と、HP の画面表示を行うデバッグ用の Initializer。インゲームのシーンへ置く。
    ///
    /// 操作は OnGUI のボタンとトグルで行う。トグルが有効な間は、移動モードが Warping に変わった瞬間にダメージを与える。
    /// HP の表示は DebugGUI.ObserveVariable に値を登録し、HP が 0 になったらログを出す。
    /// チャージ量と、管理中のかけらの数も DebugGUI.ObserveVariable で表示する。
    /// 表示には DebugGUI がシーンに必要（UsefulToolkit/ProgramTools/DebugGUI Setup）。
    /// エディタと Development Build でのみ動く。
    /// PlayerInitializer が登録する State を読む為、それより後に初期化する。
    /// </summary>
    [InitializeOrder(InitializeOrderConst.DefaultLate)]
    public sealed class PlayerDebugInitializer : InitializerBase, IInjectable<PlayerHealthService>
    {
        [SerializeField, Min(1)]
        [Tooltip("1 回に与えるダメージ量")]
        private int _damageAmount = 10;

        [SerializeField]
        [Tooltip("管理中のかけらの数の取得元")]
        private FragmentOrbAdapter _fragmentOrbAdapter;

        private PlayerHealthService _healthService;
        private IPlayerHealthState _healthState;
        private IDisposable _healthSubscription;
        private IDisposable _modeSubscription;
        private bool _isDamageOnWarpEnabled;

        public void Inject(PlayerHealthService instance)
        {
            _healthService = instance;
        }

        public override void Initialize(IBlackBoard blackBoard)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!blackBoard.TryGetStateBoard<PlayerBoard>(out var playerBoard) ||
                !playerBoard.TryGetSceneState(out _healthState, out _) ||
                !playerBoard.TryGetSceneState<IPlayerMovementState>(out var movementState, out _))
            {
                UsefulLogger.LogError(
                    "IPlayerHealthState または IPlayerMovementState が未登録の為、プレイヤーのデバッグ操作を行えません。", this);
                base.Initialize(blackBoard);
                return;
            }

            DebugGUI.ObserveVariable("Player HP", () => $"{_healthState.Current} / {_healthState.Max}");

            if (playerBoard.TryGetSceneState<IChargeState>(out var chargeState, out _))
            {
                DebugGUI.ObserveVariable("Charge", () => $"{chargeState.Current} / {chargeState.Max}");
            }
            else
            {
                UsefulLogger.LogError("IChargeState が未登録の為、チャージ量を表示できません。", this);
            }

            if (_fragmentOrbAdapter != null)
            {
                DebugGUI.ObserveVariable("Fragments", () => _fragmentOrbAdapter.FragmentCount.ToString());
            }

            _healthSubscription = _healthState.RegisterOnHealthChanged(OnHealthChanged);
            _modeSubscription = movementState.RegisterOnModeChanged(OnModeChanged);
#endif
            base.Initialize(blackBoard);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnHealthChanged(int previous, int current)
        {
            if (current <= 0)
            {
                UsefulLogger.Log($"プレイヤーの HP が 0 になりました（直前の HP : {previous}）。", this);
            }
        }

        private void OnModeChanged(PlayerMoveMode previous, PlayerMoveMode current)
        {
            if (_isDamageOnWarpEnabled && current == PlayerMoveMode.Warping)
            {
                _healthService?.ApplyDamage(_damageAmount);
            }
        }

        private void OnGUI()
        {
            if (_healthService == null || _healthState == null) return;

            const float Width = 360f;
            GUILayout.BeginArea(new Rect(10f, Screen.height - 160f, Width, 60f), GUI.skin.box);

            if (GUILayout.Button($"ダメージ ({_damageAmount})")) _healthService.ApplyDamage(_damageAmount);
            _isDamageOnWarpEnabled = GUILayout.Toggle(_isDamageOnWarpEnabled, "ワープを始めた瞬間にダメージ");

            GUILayout.EndArea();
        }
#endif

        private void OnDestroy()
        {
            _healthSubscription?.Dispose();
            _modeSubscription?.Dispose();
        }
    }
}
