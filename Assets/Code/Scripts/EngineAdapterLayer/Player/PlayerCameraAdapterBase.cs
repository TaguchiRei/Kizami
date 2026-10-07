using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 視点操作をカメラへ反映する Adapter の基底。どの方向をカメラで回すかは操作系ごとの派生が決める。
    /// </summary>
    public abstract class PlayerCameraAdapterBase : InitializableMonoBehaviour
    {
        private IDisposable _lookSubscription;

        /// <summary>
        /// PlayerInitializer から呼ばれる。PlayerLookState の登録より後に呼ぶこと。
        /// </summary>
        /// <param name="blackBoard">視点ステートの取得元</param>
        public void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetSceneState<PlayerBoard, IPlayerLookState>(out var lookState, this)) return;

            _lookSubscription = lookState.RegisterOnLookInputChanged(OnLookInputChanged);

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
        }
    }
}
