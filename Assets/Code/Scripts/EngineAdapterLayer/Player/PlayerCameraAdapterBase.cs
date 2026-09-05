using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 視点操作の上下方向をカメラへ反映する Abstractor の基底。
    /// 左右方向（体の向き）は PlayerMovementAdapterBase 側が担当し、ここでは扱わない。
    /// </summary>
    public abstract class PlayerCameraAdapterBase : InitializableMonoBehaviour
    {
        private IDisposable _lookStateWaiter;
        private IDisposable _lookSubscription;

        /// <summary>
        /// PlayerInitializer から呼ばれる。State の登録順に依存しないよう待受で拾う。
        /// </summary>
        /// <param name="playerBoard">視点ステートの取得元</param>
        public void Initialize(PlayerBoard playerBoard)
        {
            _lookStateWaiter = playerBoard.SubscribeStateRegister<IPlayerLookState>(
                () =>
                {
                    if (!playerBoard.TryGetSceneState<IPlayerLookState>(out var state, out _)) return;

                    _lookSubscription?.Dispose();
                    _lookSubscription = state.RegisterOnLookInputChanged(OnLookInputChanged);
                },
                invokeIfRegistered: true);

            // 派生の検証を通す為、base ではなく仮想メソッド側を呼ぶ
            Initialize();
        }

        /// <summary>
        /// 視点操作の入力値が変化した際に呼ばれる。
        /// </summary>
        /// <param name="lookInput">感度適用済みの入力値。x が右向き、y が上向きを正とする</param>
        protected abstract void OnLookInputChanged(Vector2 lookInput);

        protected virtual void OnDestroy()
        {
            _lookSubscription?.Dispose();
            _lookStateWaiter?.Dispose();
        }
    }
}
