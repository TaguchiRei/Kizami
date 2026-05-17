using UnityEngine;
using UsefulTools.Infrastructure.Runtime.Input;
using UsefulTools.UtilityUnity.Runtime.Initialize;
using UsefulTools.UtilityUnity.Runtime.UtilityUnity;
using UsefulVr.Application.Runtime.Player;
using UsefulVr.Domain.Runtime.Player;
using UsefulVr.Presentation.Runtime.Player;
using UsefulVr.View.Runtime.Player;

namespace UsefulVr.Composition.Runtime.Player
{
    public class VrPlayerInitializer : InitializerBase, IInjectable<IInputDispatcher>
    {
        [SerializeField] private VrPlayerMovementView _vrPlayerMovementView;
        [SerializeField] private Vector3 _gravityVector;
        [SerializeField] private float _gravityPower;
        [SerializeField] private float _moveSpeed;
        [SerializeField] private float _rotateSpeed;
        [SerializeField] private float _deadZone;
        private VrPlayerMovementService _playerMovementService;
        private VrVrPlayerMovementPresenter _vrVrPlayerPresenter;
        private VrPlayerMovementEntity _vrPlayerMovementEntity;

        //IInjectableで登録する
        private IInputDispatcher _inputDispatcher;

        public override void Initialize()
        {
            base.Initialize();
            _vrVrPlayerPresenter = new(_vrPlayerMovementView);
            _vrPlayerMovementEntity = new(
                new(_gravityVector, _gravityPower),
                new(_moveSpeed),
                _rotateSpeed, _deadZone);
            _playerMovementService = new(_vrVrPlayerPresenter, _vrPlayerMovementEntity, _inputDispatcher);

            _vrPlayerMovementView.Initialize();
        }

        public void Inject(IInputDispatcher obj)
        {
            _inputDispatcher = obj;
        }
    }
}