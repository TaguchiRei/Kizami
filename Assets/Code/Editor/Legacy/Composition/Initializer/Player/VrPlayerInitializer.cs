// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using Kizami.Application.Runtime;
using Kizami.Presentation.Runtime;
using UnityEngine;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulVr.Application.Runtime.Player;
using UsefulVr.Domain.Runtime.Player;
using UsefulVr.Presentation.Runtime.Player;
using UsefulVr.View.Runtime.Player;

namespace UsefulVr.Composition.Runtime.Player
{
    /// <summary>
    /// VR版プレイヤーの初期化クラス
    /// </summary>
    public class VrPlayerInitializer : MonoBehaviour
    {
        public PlayerInfra playerInfra;
        [SerializeField] private VrPlayerMovementView _vrPlayerMovementView;
        [SerializeField] private Vector3 _gravityVector = Vector3.down;
        [SerializeField] private float _gravityPower = 9.81f;
        [SerializeField] private float _moveSpeed = 3f;
        [SerializeField] private float _rotateSpeed = 45f;
        [SerializeField] private float _deadZone = 0.1f;

        private VrPlayerMovementService _playerMovementService;
        private VrVrPlayerMovementPresenter _vrVrPlayerPresenter;
        private VrPlayerMovementEntity _vrPlayerMovementEntity;
        private IBladePresenter _bladePresenter;

        public void Initialize(IInputDispatcher inputDispatcher, IBladePresenter bladePresenter)
        {
            _bladePresenter = bladePresenter;

            _vrVrPlayerPresenter = new(_vrPlayerMovementView);
            _vrPlayerMovementEntity = new(
                new(_gravityVector, _gravityPower),
                new(_moveSpeed),
                _rotateSpeed, _deadZone);
            _playerMovementService = new(_vrVrPlayerPresenter, _vrPlayerMovementEntity, inputDispatcher);

            _vrPlayerMovementView.Initialize();
        }

        private void FixedUpdate()
        {
            _playerMovementService?.ApplyGravity();
        }

        private void LateUpdate()
        {
            _playerMovementService?.ApplyCameraOffset();
        }
    }
}
#endif
