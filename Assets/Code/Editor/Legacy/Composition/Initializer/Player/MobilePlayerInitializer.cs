// [Legacy] 作り直しに伴い全体を無効化
#if false
using System;
using Kizami.Application.Runtime;
using Kizami.Domain.Runtime.Player;
using Kizami.Presentation.Runtime;
using Kizami.Presentation.Runtime.Player;
using Kizami.View.Runtime.Player.Player;
using UnityEngine;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulVr.Domain.Runtime.Domain;
using Code.Scripts.Domain.Player;
using Kizami.Application.Runtime.Input;

namespace Kizami.Composition.Runtime.Player
{
    /// <summary>
    /// モバイル版プレイヤーの初期化クラス
    /// </summary>
    public class MobilePlayerInitializer : MonoBehaviour
    {
        public PlayerInfra _playerInfra;
        [SerializeField] private MobilePlayerView _mobilePlayerView;
        [SerializeField] private Vector3 _gravityVector = Vector3.down;
        [SerializeField] private float _gravityPower = 9.81f;
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _lookSensitivity = 0.5f;

        private MobilePlayerService _playerService;
        private MobilePlayerPresenter _mobilePlayerPresenter;
        private MobilePlayerMovementEntity _mobilePlayerMovementEntity;

        public void Initialize(IInputDispatcher inputDispatcher, IBladePresenter bladePresenter)
        {
            _mobilePlayerPresenter = new MobilePlayerPresenter(_mobilePlayerView);
            _mobilePlayerMovementEntity = new MobilePlayerMovementEntity(
                new GravityValue(_gravityVector, _gravityPower),
                new MoveSpeed(_moveSpeed),
                _lookSensitivity);

            _playerService = new MobilePlayerService(
                inputDispatcher,
                bladePresenter,
                _mobilePlayerPresenter,
                _mobilePlayerMovementEntity,
                _playerInfra);
            _playerInfra.Initialize();
        }

        private void FixedUpdate()
        {
            _playerService?.ApplyGravity();
        }

        private void OnDestroy()
        {
            _playerService.Dispose();
        }
    }
}
#endif
