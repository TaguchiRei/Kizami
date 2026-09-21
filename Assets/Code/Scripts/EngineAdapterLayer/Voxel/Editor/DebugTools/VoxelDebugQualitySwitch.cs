using UnityEngine;
using UnityEngine.InputSystem;

namespace Kizami.EngineAdapter.Voxel.DebugTools
{
    /// <summary>
    /// Tab キーで VoxelModelLoader の品質設定を順に切り替え、読み込み直す検証用コンポーネント。
    /// </summary>
    public sealed class VoxelDebugQualitySwitch : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("読み込み直す対象")]
        private VoxelModelLoader _loader;

        [SerializeField]
        [Tooltip("切り替える品質設定。Tab を押すたびに次のものへ移る")]
        private VoxelQualitySettings[] _qualities;

        private int _index;
        private double _lastLoadMilliseconds;

        private void Start()
        {
            if (_loader == null || _qualities == null) return;

            _index = Mathf.Max(System.Array.IndexOf(_qualities, _loader.Quality), 0);
        }

        private void Update()
        {
            if (_loader == null || _qualities == null || _qualities.Length == 0) return;

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame) return;

            _index = (_index + 1) % _qualities.Length;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _loader.SetQuality(_qualities[_index]);
            _loader.Load();
            _lastLoadMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        private void OnGUI()
        {
            if (!VoxelDebugHud.IsVisible || _loader == null || _loader.Quality == null) return;

            var quality = _loader.Quality;
            GUI.Box(new Rect(10f, 205f, 560f, 26f),
                $"Tab: 品質の切り替え  現在: {quality.name}（{quality.VoxelSize:0.000} m）  " +
                $"読み込み: {_lastLoadMilliseconds:0.0} ms");
        }
    }
}
