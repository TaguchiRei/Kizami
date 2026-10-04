using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 近接切断の切断面の角度を、画面の中央に置いた線の傾きとして表示する。
    /// </summary>
    public sealed class MeleeCutPreviewAdapter : InitializableMonoBehaviour
    {
        [SerializeField]
        [Tooltip("切断面を表す線。角度 0 で水平になる向きで置く")]
        private RectTransform _line;

        private IDisposable _stateWaiter;
        private IMeleeCutState _state;

        /// <summary>
        /// PlayerInitializer から呼ばれる。State の登録順に依存しないよう待受で拾う。
        /// </summary>
        /// <param name="playerBoard">切断面の角度の取得元</param>
        public void Initialize(PlayerBoard playerBoard)
        {
            _stateWaiter = playerBoard.SubscribeStateRegister<IMeleeCutState>(
                () =>
                {
                    if (playerBoard.TryGetSceneState<IMeleeCutState>(out var state, out _))
                    {
                        _state = state;
                    }
                },
                invokeIfRegistered: true);

            if (_line == null)
            {
                UsefulLogger.LogError("切断面の線（RectTransform）が設定されていません。", this);
            }

            Initialize();
        }

        private void Update()
        {
            if (_state == null || _line == null) return;

            _line.localRotation = Quaternion.Euler(0f, 0f, _state.Angle);
        }

        private void OnDestroy()
        {
            _stateWaiter?.Dispose();
        }
    }
}
