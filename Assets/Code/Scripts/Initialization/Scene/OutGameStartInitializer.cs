using Kizami.Application;
using UnityEngine;
using UsefulToolkit.Initialization;

namespace Kizami.Initialization
{
    /// <summary>
    /// 仮のアウトゲームに、ステージを選んでインゲームへ遷移するボタンを並べる Initializer。仮のアウトゲームの場面シーンへ置く。
    /// </summary>
    // TODO: アウトゲーム本来の流れに置き換える
    public sealed class OutGameStartInitializer : InitializerBase, IInjectable<IGameSceneController>
    {
        private const float BUTTON_WIDTH = 200f;
        private const float BUTTON_HEIGHT = 50f;

        private IGameSceneController _sceneController;
        private bool _isTransitioning;

        private void OnGUI()
        {
            if (_sceneController == null || _isTransitioning) return;

            var stages = _sceneController.Stages;
            var area = new Rect(Screen.width * 0.5f - BUTTON_WIDTH * 0.5f,
                Screen.height * 0.5f - BUTTON_HEIGHT * stages.Count * 0.5f,
                BUTTON_WIDTH, BUTTON_HEIGHT * stages.Count);

            GUILayout.BeginArea(area);
            for (var i = 0; i < stages.Count; i++)
            {
                if (stages[i] == null) continue;

                if (GUILayout.Button(stages[i].DisplayName, GUILayout.Height(BUTTON_HEIGHT)))
                {
                    GoToStage(i);
                }
            }

            GUILayout.EndArea();
        }

        private async void GoToStage(int stageIndex)
        {
            _isTransitioning = true;
            // このオブジェクトは遷移中のアンロードで破棄される。destroyCancellationToken を渡すと
            // アンロードの途中で中断され、SceneState に OutGame がロード済みのまま残る為、トークンは渡さない。
            await _sceneController.GoToStageAsync(stageIndex);
            _isTransitioning = false;
        }

        public void Inject(IGameSceneController instance)
        {
            _sceneController = instance;
        }
    }
}
