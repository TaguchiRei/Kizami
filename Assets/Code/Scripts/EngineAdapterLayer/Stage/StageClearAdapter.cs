using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージシーンの破壊対象（DestructionTarget）を集め、破壊済みの数が変わるたびに、破壊対象の数と破壊済みの数を通知する Adapter。インゲームのシーンへ置く。
    /// クリアの判定は通知を受ける Application 側が行う。
    /// </summary>
    public sealed class StageClearAdapter : InitializableMonoBehaviour
    {
        private readonly List<DestructionTarget> _targets = new();

        private Action<int, int> _onTargetsChanged;
        private int _destroyedCount = -1;

        /// <summary> ステージシーンにある破壊対象 </summary>
        public IReadOnlyList<DestructionTarget> Targets => _targets;

        /// <summary>
        /// ステージシーンの破壊対象を集め、最初の数を通知する。ステージシーンを読み込んだあとに呼ぶ。
        /// </summary>
        /// <param name="onTargetsChanged">破壊済みの数が変わったときに呼ぶ処理。引数は破壊対象の数と破壊済みの数</param>
        public void Initialize(Action<int, int> onTargetsChanged)
        {
            _onTargetsChanged = onTargetsChanged;
            _targets.Clear();
            _targets.AddRange(FindObjectsByType<DestructionTarget>(FindObjectsSortMode.None));

            if (_targets.Count == 0)
            {
                UsefulLogger.LogWarning("ステージに破壊対象がない為、クリアになりません。", this);
            }

            _destroyedCount = -1;
            base.Initialize();
            CountDestroyed();
        }

        private void Update()
        {
            if (!Initialized) return;

            CountDestroyed();
        }

        /// <summary>
        /// 破壊済みの破壊対象を数え、前回から変わっていたら通知する。
        /// </summary>
        private void CountDestroyed()
        {
            var destroyedCount = 0;
            foreach (var target in _targets)
            {
                if (target == null || target.IsDestroyed) destroyedCount++;
            }

            if (destroyedCount == _destroyedCount) return;

            _destroyedCount = destroyedCount;
            _onTargetsChanged?.Invoke(_targets.Count, destroyedCount);
        }
    }
}
