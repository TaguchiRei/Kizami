using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージシーンの破壊対象（DestructionTarget）を集め、すべて破壊済みになったらクリアにして、仮のクリア表示を出す Adapter。インゲームのシーンへ置く。
    /// クリアを読むのは仮の表示だけなので、判定もこの Adapter が持つ。
    /// </summary>
    // TODO: リザルトやリトライがクリアを読むようになったら（区間11）、判定を Application の Service と State に移す
    public sealed class StageClearAdapter : InitializableMonoBehaviour
    {
        private const string CLEAR_TEXT = "STAGE CLEAR";
        private const int CLEAR_FONT_SIZE = 64;

        private readonly List<DestructionTarget> _targets = new();

        private GUIStyle _clearStyle;

        /// <summary> すべての破壊対象が破壊済みになったか。一度クリアになったら戻らない </summary>
        public bool IsCleared { get; private set; }

        /// <summary> ステージシーンにある破壊対象 </summary>
        public IReadOnlyList<DestructionTarget> Targets => _targets;

        /// <summary> 破壊済みの破壊対象の数 </summary>
        public int DestroyedTargetCount { get; private set; }

        /// <summary>
        /// ステージシーンの破壊対象を集める。ステージシーンを読み込んだあとに呼ぶ。
        /// </summary>
        public override void Initialize()
        {
            _targets.Clear();
            _targets.AddRange(FindObjectsByType<DestructionTarget>(FindObjectsSortMode.None));

            if (_targets.Count == 0)
            {
                UsefulLogger.LogWarning("ステージに破壊対象がない為、クリアになりません。", this);
            }

            base.Initialize();
        }

        private void Update()
        {
            if (IsCleared || _targets.Count == 0) return;

            var destroyedCount = 0;
            foreach (var target in _targets)
            {
                if (target == null || target.IsDestroyed) destroyedCount++;
            }

            DestroyedTargetCount = destroyedCount;
            if (destroyedCount < _targets.Count) return;

            IsCleared = true;
            UsefulLogger.Log("すべての破壊対象が破壊済みになった為、クリアにしました。", this);
        }

        private void OnGUI()
        {
            if (!IsCleared) return;

            _clearStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = CLEAR_FONT_SIZE,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), CLEAR_TEXT, _clearStyle);
        }
    }
}
