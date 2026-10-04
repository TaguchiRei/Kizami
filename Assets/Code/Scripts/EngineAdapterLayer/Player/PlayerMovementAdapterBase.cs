using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// プレイヤーの移動と視点を Transform / Rigidbody へ反映する Abstractor の基底。
    /// 視点操作も移動の一種として同じコンポーネントが受け持つ。
    ///
    /// 水平移動の反映は操作系によらず共通なのでここに置き、
    /// 視点入力をどう回転へ変換するか（何度回すか、上下を使うか、時間で積分するか）だけを
    /// 派生＝操作系ごとの実装が決める。
    /// </summary>
    public abstract class PlayerMovementAdapterBase : InitializableMonoBehaviour
    {
        [SerializeField] private Rigidbody _rigidbody;

        [Header("水平速度の補間レート (m/s^2)")]
        [SerializeField, Min(0f)] private float _acceleration = 40f;
        [SerializeField, Min(0f)] private float _deceleration = 60f;

        private IPlayerMovementState _movementState;
        private Action<Vector3> _step;
        private IDisposable _movementStateWaiter;
        private IDisposable _lookStateWaiter;
        private IDisposable _lookSubscription;
        private Vector3 _horizontalVelocity;

        /// <summary>
        /// PlayerInitializer から呼ばれる。State の登録順に依存しないよう待受で拾う。
        /// </summary>
        /// <param name="playerBoard">移動・視点ステートの取得元</param>
        /// <param name="step">FixedUpdate ごとに視線の向きを渡して呼ぶ処理（PlayerMovementService.Step）</param>
        public void Initialize(PlayerBoard playerBoard, Action<Vector3> step)
        {
            _step = step;

            _movementStateWaiter = playerBoard.SubscribeStateRegister<IPlayerMovementState>(
                () =>
                {
                    if (playerBoard.TryGetSceneState<IPlayerMovementState>(out var state, out _))
                    {
                        _movementState = state;
                    }
                },
                invokeIfRegistered: true);

            _lookStateWaiter = playerBoard.SubscribeStateRegister<IPlayerLookState>(
                () =>
                {
                    if (!playerBoard.TryGetSceneState<IPlayerLookState>(out var state, out _)) return;

                    _lookSubscription?.Dispose();
                    _lookSubscription = state.RegisterOnLookInputChanged(OnLookInputChanged);
                },
                invokeIfRegistered: true);

            if (_rigidbody == null)
            {
                UsefulLogger.LogError("Rigidbody が設定されていません。", this);
            }

            // 派生の検証を通す為、base ではなく仮想メソッド側を呼ぶ
            Initialize();
        }

        /// <summary>
        /// 視点操作の入力値が変化した際に呼ばれる。
        /// </summary>
        /// <param name="lookInput">感度適用済みの入力値。x が右向き、y が上向きを正とする</param>
        protected abstract void OnLookInputChanged(Vector2 lookInput);

        /// <summary>
        /// ワールド空間の視線の向きを返す。既定ではこの Transform の前方を返す。
        /// </summary>
        protected virtual Vector3 GetViewDirection() => transform.forward;

        /// <summary>
        /// 視線の向きを渡して PlayerMovementService.Step を呼んだあと、
        /// 目標速度（PlayerMovementState.TargetVelocity の水平成分）へ向けて水平速度を緩やかに補間する。
        /// 移動開始直後は即座に目標速度へ到達させず、加速レートで徐々に近づけていく。
        /// Y 軸方向の速度（重力・ジャンプ等）は上書きせず、Rigidbody の現在値をそのまま通す。
        /// </summary>
        private void FixedUpdate()
        {
            _step?.Invoke(GetViewDirection());

            if (_movementState == null || _rigidbody == null) return;

            var target = _movementState.TargetVelocity;
            target.y = 0f;

            // 目標へ近づく（加速）ときは加速レート、緩める・止める（減速）ときは減速レートを使う
            var rate = target.sqrMagnitude >= _horizontalVelocity.sqrMagnitude ? _acceleration : _deceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, target, rate * Time.fixedDeltaTime);

            var current = _rigidbody.linearVelocity;
            _rigidbody.linearVelocity = new Vector3(_horizontalVelocity.x, current.y, _horizontalVelocity.z);
        }

        protected virtual void OnDestroy()
        {
            _lookSubscription?.Dispose();
            _lookStateWaiter?.Dispose();
            _movementStateWaiter?.Dispose();
        }
    }
}
