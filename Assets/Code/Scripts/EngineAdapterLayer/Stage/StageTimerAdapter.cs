using System;
using UnityEngine;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 毎フレームの実時間を、初期化で受け取った処理へ渡す Adapter。インゲームのシーンへ置く。
    /// 制限時間を進めるのは受け取る側（Application）で、時間を止めても進むように、時間の倍率の影響を受けない実時間を渡す。
    /// </summary>
    public sealed class StageTimerAdapter : InitializableMonoBehaviour
    {
        private Action<float> _onTick;

        /// <summary>
        /// 毎フレームの実時間を渡す先を設定する。
        /// </summary>
        /// <param name="onTick">毎フレーム呼ぶ処理。引数は前回から経った実時間（秒）</param>
        public void Initialize(Action<float> onTick)
        {
            _onTick = onTick;
            base.Initialize();
        }

        private void Update()
        {
            if (!Initialized) return;

            _onTick?.Invoke(Time.unscaledDeltaTime);
        }
    }
}
