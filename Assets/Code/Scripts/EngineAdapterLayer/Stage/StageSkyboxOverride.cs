using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ステージシーンが読み込まれている間だけ、天球のマテリアルを差し替える。ステージシーンへ置く。
    /// 天球はアクティブなシーンのライティング設定で決まり、主のシーン（町）がアクティブになると戻る為、アクティブなシーンが変わるたびに差し替え直す。
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
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Apply();
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            if (_skybox == null || RenderSettings.skybox != _skybox) return;

            RenderSettings.skybox = _previousSkybox;
            DynamicGI.UpdateEnvironment();
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            Apply();
        }

        /// <summary>
        /// 天球を差し替える。すでに差し替えていれば何もしない。
        /// </summary>
        private void Apply()
        {
            if (_skybox == null || RenderSettings.skybox == _skybox) return;

            _previousSkybox = RenderSettings.skybox;
            RenderSettings.skybox = _skybox;
            DynamicGI.UpdateEnvironment();
        }
    }
}
