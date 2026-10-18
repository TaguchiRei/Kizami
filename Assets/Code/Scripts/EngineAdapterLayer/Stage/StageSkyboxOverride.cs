using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージシーンが読み込まれている間だけ、天球のマテリアルを差し替える。ステージシーンへ置く。
    /// 追加で読み込んだシーンのライティング設定は使われず、主のシーン（町）の天球が出る為、実行時に RenderSettings を書き換える。
    /// </summary>
    public sealed class StageSkyboxOverride : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("このステージで使う天球のマテリアル")]
        private Material _skybox;

        /// <summary> 差し替える前の天球。外れたときに戻す </summary>
        private Material _previousSkybox;

        private void OnEnable()
        {
            if (_skybox == null) return;

            _previousSkybox = RenderSettings.skybox;
            RenderSettings.skybox = _skybox;
            DynamicGI.UpdateEnvironment();
        }

        private void OnDisable()
        {
            if (_skybox == null || RenderSettings.skybox != _skybox) return;

            RenderSettings.skybox = _previousSkybox;
            DynamicGI.UpdateEnvironment();
        }
    }
}
