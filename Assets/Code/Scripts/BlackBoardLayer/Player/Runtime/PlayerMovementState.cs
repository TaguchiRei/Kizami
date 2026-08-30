using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    public class PlayerMovementState : SceneStateBase, IPlayerMovementState
    {
        public Vector3 MovementDirection => _movementDirection;
        public float MovementSpeed => _movementSpeed;

        private Vector3 _movementDirection;
        private float _movementSpeed;

        private Action<float> _changeMovementSpeedCallback;

        public void ChangeMovementSpeed(float speed)
        {
            _movementSpeed = speed;
            _changeMovementSpeedCallback?.Invoke(speed);
        }

        public void ChangeMovementDirection(Vector3 movementDirection)
        {
            _movementDirection = movementDirection;
        }

        public IDisposable RegisterChangeMovementSpeed(Action<float> callback)
        {
            _changeMovementSpeedCallback += callback;
            return new BoardDispose(() => _changeMovementSpeedCallback -= callback);
        }

        public override string GetLog()
        {
            return $"MovementDirection: {MovementDirection}  \nMovementSpeed: {MovementSpeed}";
        }
    }

    public interface IPlayerMovementState : IStateGetter
    {
        public Vector3 MovementDirection { get; }
        public float MovementSpeed { get; }

        /// <summary>
        /// 移動速度が変化した際に発火するイベントを登録する
        /// </summary>
        /// <param name="callback"></param>
        public IDisposable RegisterChangeMovementSpeed(Action<float> callback);
    }
}