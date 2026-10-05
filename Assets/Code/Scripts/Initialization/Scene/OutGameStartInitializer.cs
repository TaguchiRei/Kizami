using Kizami.Application;
using UnityEngine;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// 仮のアウトゲームから、インゲームへ遷移するボタンを画面に出す Initializer。仮のアウトゲームの場面シーンへ置く。
    /// </summary>
    // TODO: アウトゲーム本来の流れに置き換える
    public sealed class OutGameStartInitializer : InitializerBase, IInjectable<IGameSceneController>
    {
        private IGameSceneController _sceneController;
        private bool _transitioning;

        private async void OnGUI()
        {
            if (_sceneController == null || _transitioning) return;

            var area = new Rect(Screen.width * 0.5f - 100f, Screen.height * 0.5f - 25f, 200f, 50f);

            if (!GUI.Button(area, "インゲームへ")) return;

            _transitioning = true;
            // このオブジェクトは遷移中のアンロードで破棄される。destroyCancellationToken を渡すと
            // アンロードの途中で中断され、SceneState に OutGame がロード済みのまま残る為、トークンは渡さない。
            await _sceneController.GoToInGameAsync();
            _transitioning = false;
        }

        public void Inject(IGameSceneController instance)
        {
            _sceneController = instance;
        }
    }
}
