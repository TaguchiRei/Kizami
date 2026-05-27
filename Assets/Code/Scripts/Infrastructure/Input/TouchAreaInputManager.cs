using System;
using UnityEngine;
using UnityEngine.UI;
using UsefulTools.Application.Runtime;
using UsefulTools.Application.Runtime.Input;

namespace UsefulTools.Infrastructure.Runtime.Input
{
    /// <summary>
    /// タッチエリア入力システムを管理するクラス。
    /// </summary>
    public sealed class TouchAreaInputManager
    {
        private GraphicRaycaster _raycaster;

        private TouchAreaUseCase _useCase;
        private EnhancedTouchInputInfra _infra;
        private TouchAreaManagement _management;

        public void SetRaycaster(GraphicRaycaster raycaster)
        {
            _raycaster = raycaster;
        }

        public void StartGame()
        {
            if (_raycaster == null)
            {
                Debug.LogError("[TouchAreaInputManager] GraphicRaycasterが設定されていません。");
                return;
            }

            if (_management == null) _management = new TouchAreaManagement(_raycaster);
            if (_infra == null) _infra = new EnhancedTouchInputInfra();
            if (_useCase == null) _useCase = new TouchAreaUseCase(_infra, _management);

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

        public void Tick()
        {
            _infra?.Update();
        }

        public void LateTick()
        {
            _useCase?.LateTick();
        }
    }
}