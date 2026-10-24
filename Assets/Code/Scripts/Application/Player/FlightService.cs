using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 視線の先の飛び移りの目印を受け取って F キーの誘導を出し、F が押されたときに目印へ飛ぶユースケース。
    /// F は普段スローモードの入力なので、飛び移れるときだけ先にここで受け取る。
    /// 着地の周囲のボクセルを壊す目印なら、飛び始めに先に壊す。余裕の高さがある目印へは、真上へ上がってから目印へ下りる。
    /// </summary>
    public sealed class FlightService
    {
        private readonly FlightPromptState _state = new();
        private readonly PlayerMovementService _movementService;
        private readonly float _flightSpeed;
        private readonly Func<Vector3> _getBodyCenter;
        private readonly Action<Vector3, float> _carveExplosion;

        private FlightTarget? _candidate;

        /// <param name="blackBoard">FlightPromptState の登録先</param>
        /// <param name="movementService">飛び始められるかを見て、飛行を要求する先</param>
        /// <param name="flightSpeed">飛ぶ速さ（m/s）</param>
        /// <param name="getBodyCenter">プレイヤーの体の中心（ワールド空間）を返す関数。飛び始める位置に使う</param>
        /// <param name="carveExplosion">中心と半径を受け取り、その球のボクセルを削る関数</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public FlightService(IBlackBoard blackBoard, PlayerMovementService movementService, float flightSpeed,
            Func<Vector3> getBodyCenter, Action<Vector3, float> carveExplosion, int sceneId)
        {
            _movementService = movementService;
            _flightSpeed = flightSpeed;
            _getBodyCenter = getBodyCenter;
            _carveExplosion = carveExplosion;

            if (!blackBoard.TryGetBoard<PlayerBoard>(out var playerBoard, this)) return;

            playerBoard.RegisterSceneState<IFlightPromptState>(_state, sceneId);
        }

        /// <summary>
        /// 視線の先の目印を設定する。毎フレーム、見つけた目印か null を渡す。
        /// </summary>
        public void SetCandidate(FlightTarget? candidate)
        {
            _candidate = candidate;
            _state.SetVisible(candidate.HasValue);
        }

        /// <summary>
        /// 視線の先に目印があり、飛び始められるなら飛ぶ。
        /// </summary>
        /// <returns>飛んだら true。飛ばなかったときは false で、F は他の入力として扱う</returns>
        public bool TryFly()
        {
            if (!_candidate.HasValue || !_movementService.CanRush) return false;

            Fly(_candidate.Value);
            return true;
        }

        /// <summary>
        /// 目印へ飛ぶ。目印が壁に取りつく目印なら、着いたその場で壁に取りつく。
        /// </summary>
        private void Fly(FlightTarget target)
        {
            var start = _getBodyCenter();
            if (target.CarvesVoxels) _carveExplosion?.Invoke(target.Position, target.CarveRadius);

            var riseHeight = Mathf.Max(0f, target.Position.y + target.ApproachClearance - start.y);
            if (target.ApproachClearance > 0f && riseHeight > 0f)
            {
                var rise = Vector3.up * riseHeight;
                var descent = target.Position - (start + rise);
                _movementService.RequestFlight(rise, rise.magnitude / _flightSpeed, descent,
                    descent.magnitude / _flightSpeed);
                return;
            }

            var displacement = target.Position - start;
            _movementService.RequestFlight(displacement, displacement.magnitude / _flightSpeed, Vector3.zero, 0f);
        }
    }
}
