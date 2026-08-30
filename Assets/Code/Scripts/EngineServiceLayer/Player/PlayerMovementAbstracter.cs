using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineService
{
    /// <summary>
    /// PlayerMovementState を購読し、PlayerRoot の Rigidbody へ移動を反映する。
    ///
    /// 目標速度（MovementDirection * MovementSpeed）へ向けて水平速度を毎 FixedUpdate 緩やかに補間する。
    /// 移動開始直後は即座に目標速度へ到達させず、加速レートで徐々に近づけていく。到達上限は MovementSpeed。
    /// 加速レートと減速レートは個別に調整できる。
    /// Y 軸方向の速度（重力・ジャンプ等）は上書きせず、Rigidbody の現在値をそのまま通す。
    /// </summary>
    public sealed class PlayerMovementAbstracter : InitializableMonoBehaviour
    {
        [SerializeField] private Rigidbody _rigidbody;

        [Header("水平速度の補間レート (m/s^2)")]
        [SerializeField, Min(0f)] private float _acceleration = 40f;
        [SerializeField, Min(0f)] private float _deceleration = 60f;

        private IPlayerMovementState _movementState;
        private IDisposable _stateWaiter;
        private Vector3 _horizontalVelocity;

        /// <summary>
        /// PlayerInitializer から呼ばれる。State の登録順に依存しないよう待受で拾う。
        /// </summary>
        public void Initialize(PlayerBoard playerBoard)
        {
            _stateWaiter = playerBoard.SubscribeStateRegister<IPlayerMovementState>(
                () =>
                {
                    if (playerBoard.TryGetSceneState<IPlayerMovementState>(out var state, out _))
                    {
                        _movementState = state;
                    }
                },
                invokeIfRegistered: true);

            if (_rigidbody == null)
            {
                UsefulLogger.LogError("Rigidbody が設定されていません。", this);
            }

            base.Initialize();
        }

        private void FixedUpdate()
        {
            if (_movementState == null || _rigidbody == null) return;

            var target = _movementState.MovementDirection * _movementState.MovementSpeed;
            target.y = 0f;

            // 目標へ近づく（加速）ときは加速レート、緩める・止める（減速）ときは減速レートを使う
            var rate = target.sqrMagnitude >= _horizontalVelocity.sqrMagnitude ? _acceleration : _deceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, target, rate * Time.fixedDeltaTime);

            var current = _rigidbody.linearVelocity;
            _rigidbody.linearVelocity = new Vector3(_horizontalVelocity.x, current.y, _horizontalVelocity.z);
        }

        private void OnDestroy()
        {
            _stateWaiter?.Dispose();
        }
    }
}
