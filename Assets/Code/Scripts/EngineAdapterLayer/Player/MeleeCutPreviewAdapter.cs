using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
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

        private IMeleeCutState _state;

        /// <summary>
        /// PlayerInitializer から呼ばれる。MeleeCutState の登録より後に呼ぶこと。
        /// </summary>
        /// <param name="blackBoard">切断面の角度の取得元</param>
        public void Initialize(IBlackBoard blackBoard)
        {
            if (_line == null)
            {
                UsefulLogger.LogError("切断面の線（RectTransform）が設定されていません。", this);
                return;
            }

            if (!blackBoard.TryGetSceneState<PlayerBoard, IMeleeCutState>(out _state, this)) return;

            Initialize();
        }

        private void Update()
        {
            _line.localRotation = Quaternion.Euler(0f, 0f, _state.Angle);
        }
    }
}
