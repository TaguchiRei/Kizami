using UnityEngine;
using UsefulToolkit.BlackBoard.Logger;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// PC / スマホ用の移動反映。移動の向きはカメラの前方から決める。
    /// 視点の回転は上下・左右とも StandardPlayerCameraAdapter がカメラ側で反映し、体は回さない。
    /// 補間を有効にした Rigidbody の向きを Transform から書き換えると、物理の姿勢の書き戻しで元の向きへ引き戻されることがある為。
    /// </summary>
    public sealed class StandardPlayerMovementAdapter : PlayerMovementAdapterBase
    {
        [Header("視点")]
        [SerializeField]
        [Tooltip("視線の向きの取得元。CinemachineCamera。体の子である必要がある。")]
        private Transform _cameraTransform;

        public override void Initialize()
        {
            base.Initialize();

            if (_cameraTransform == null)
            {
                UsefulLogger.LogError("カメラの Transform が設定されていません。", this);
            }
        }

        /// <summary>
        /// カメラの前方を視線の向きとして返す。カメラが未設定ならこの Transform の前方を返す。
        /// </summary>
        protected override Vector3 GetViewDirection()
        {
            return _cameraTransform != null ? _cameraTransform.forward : transform.forward;
        }
    }
}
