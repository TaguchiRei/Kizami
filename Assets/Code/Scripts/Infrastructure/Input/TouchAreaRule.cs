using System;
using UnityEngine;
using UnityEngine.UI;
using UsefulTools.Application.Runtime;
using UsefulTools.Application.Runtime.Input;

namespace UsefulTools.Infrastructure.Runtime.Input
{
    /// <summary>
    /// タッチエリア入力システムを管理するルール実装。
    /// SceneRuleなどのIRule管理クラスに登録して利用する。
    /// </summary>
    [Serializable]
    public sealed class TouchAreaRule : IUpdateRule
    {
        [SerializeField] private GraphicRaycaster _raycaster;

        private TouchAreaUseCase _useCase;
        private EnhancedTouchInputInfra _infra;
        private TouchAreaManagement _management;

        public RuleState State => _infra?.State ?? RuleState.Playing;

        public event Action<RuleState> OnGameEndAction;

        public void StartGame()
        {
            if (_raycaster == null)
            {
                Debug.LogError("[TouchAreaRule] GraphicRaycasterが設定されていません。");
                return;
            }

            _management = new TouchAreaManagement(_raycaster);
            _infra = new EnhancedTouchInputInfra();
            _useCase = new TouchAreaUseCase(_infra, _management);

            _infra.StartGame();
        }

        public void Pause()
        {
            _infra?.Pause();
        }

        public void Resume()
        {
            _infra?.Resume();
        }

        public void Stop()
        {
            _infra?.Stop();
        }

        public void Update()
        {
            // 物理タッチ入力をUseCaseに流し込む
            _infra?.Update();
            
            // フレームの最後に仮想デバイスのDelta値をリセットする
            // 注意: このUpdateが実行されるタイミングにより、Deltaの有効期間が決まる
            _useCase?.LateTick();
        }
    }
}