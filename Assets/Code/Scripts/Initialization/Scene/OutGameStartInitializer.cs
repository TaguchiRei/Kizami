using Kizami.Application;
using UnityEngine;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// 仮のアウトゲームから、インゲームへ遷移するボタンを画面に出す Initializer。仮のアウトゲームの場面シーンへ置く。
    /// 区間11で、アウトゲーム本来の流れに置き換える。
    /// </summary>
    public sealed class OutGameStartInitializer : InitializerBase, IInjectable<IGameSceneController>
    {
        private IGameSceneController _sceneController;
        private bool _transitioning;

        public void Inject(IGameSceneController instance)
        {
            _sceneController = instance;
        }

        private async void OnGUI()
        {
            if (_sceneController == null || _transitioning) return;

            var area = new Rect(Screen.width * 0.5f - 100f, Screen.height * 0.5f - 25f, 200f, 50f);

            if (!GUI.Button(area, "インゲームへ")) return;

            _transitioning = true;
            await _sceneController.GoToInGameAsync(destroyCancellationToken);
            _transitioning = false;
        }
    }
}
