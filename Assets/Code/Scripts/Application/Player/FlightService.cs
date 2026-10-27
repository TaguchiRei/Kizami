using System;
using Kizami.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 視線の先の飛び移りの目印を受け取って F キーの誘導を出し、F が押されたときに目印へ瞬間移動するユースケース。
    /// F は普段スローモードの入力なので、飛び移れるときだけ先にここで受け取る。
    /// 着地の周囲のボクセルを壊す目印なら、移動の前に先に壊す。
    /// </summary>
    public sealed class FlightService
    {
        private readonly FlightPromptState _state = new();
        private readonly IPlayerMovementController _movementService;
        private readonly Action<Vector3> _teleport;
        private readonly Func<Vector3> _getBodyCenter;
        private readonly PlayerSkillEffectEvents _effectEvents;

        private FlightTarget? _candidate;

        /// <param name="blackBoard">FlightPromptState の登録先</param>
        /// <param name="movementService">飛び始められるかを見て、移動後に通知する先</param>
        /// <param name="teleport">プレイヤーを今の位置からの相対で動かす関数。瞬間移動する</param>
        /// <param name="getBodyCenter">プレイヤーの体の中心（ワールド空間）を返す関数。移動量を求めるのに使う</param>
        /// <param name="effectEvents">着地の周囲のボクセルを球で削らせる出来事の流し先</param>
        /// <param name="sceneId">State を紐づけるシーンのビルドインデックス</param>
        public FlightService(IBlackBoard blackBoard, IPlayerMovementController movementService,
            Action<Vector3> teleport, Func<Vector3> getBodyCenter, PlayerSkillEffectEvents effectEvents, int sceneId)
        {
            _movementService = movementService;
            _teleport = teleport;
            _getBodyCenter = getBodyCenter;
            _effectEvents = effectEvents;

            if (!blackBoard.TryGetBoard<PlayerBoard>(out var playerBoard, this)) return;

            playerBoard.RegisterSceneState<IFlightPromptState>(_state, sceneId);
        }

        /// <summary>
        /// 視線の先の目印を設定する。毎フレーム、見つけた目印か null を渡す。
        /// </summary>
        public void SetCandidate(FlightTarget? candidate)
        {
            _candidate = candidate;
            _state.SetTarget(candidate.HasValue, candidate.HasValue ? candidate.Value.Position : Vector3.zero);
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
        /// 目印へ瞬間移動する。
        /// </summary>
        private void Fly(FlightTarget target)
        {
            if (target.CarvesVoxels) _effectEvents.RaiseSphereCarve(new SphereCarveNotice(target.Position, target.CarveRadius));

            _teleport(target.Position - _getBodyCenter());
            _movementService.OnTeleported();
        }
    }
}
