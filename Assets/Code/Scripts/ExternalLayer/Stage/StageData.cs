using UnityEngine;
using UsefulToolkit.External.Scene;

namespace Kizami.External
{
    /// <summary>
    /// ステージ 1 つの定義。アウトゲームの選択肢に出す名前と、インゲームで読み込むシーングループを持つ。
    /// 敵の出し方と破壊対象は、ステージシーンに置いた物が持つ。
    /// </summary>
    [CreateAssetMenu(fileName = "StageData", menuName = "Kizami/Stage/StageData")]
    public sealed class StageData : ScriptableObject
    {
        [SerializeField]
        [Tooltip("アウトゲームの選択肢に出す名前")]
        private string _displayName = "Stage";

        [SerializeField]
        [Tooltip("インゲームで読み込むシーングループ。ステージシーンをアクティブにし、InGame を加える")]
        private SceneGroupDataBase _sceneGroup;

        /// <summary> アウトゲームの選択肢に出す名前 </summary>
        public string DisplayName => _displayName;

        /// <summary> インゲームで読み込むシーングループ </summary>
        public SceneGroupDataBase SceneGroup => _sceneGroup;
    }
}
