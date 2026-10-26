using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// プレイヤーが始まる位置の目印。ステージシーンへ1つ置く。置いていないステージでは、プレイヤーはインゲームのシーンに置いた位置から始まる。
    /// 位置はプレイヤーの Transform を置く位置で、Y 軸の向きは始まるときの視点の向き。
    /// </summary>
    public sealed class PlayerStartPoint : MonoBehaviour
    {
        /// <summary> 目印の球の半径（m） </summary>
        private const float GIZMO_RADIUS = 0.5f;

        /// <summary> 目印の球から地面へ引く線の長さ（m） </summary>
        private const float GIZMO_LINE_LENGTH = 1f;

        /// <summary>
        /// プレイヤーをこの位置へ置き、視点をこの向きにする。
        /// </summary>
        /// <param name="movementAdapter">プレイヤーの移動の Adapter</param>
        /// <param name="cameraAdapter">視点の Adapter。なければ向きは変えない</param>
        public void Place(PlayerMovementAdapterBase movementAdapter, PlayerCameraAdapterBase cameraAdapter)
        {
            movementAdapter.PlaceAt(transform.position);
            if (cameraAdapter != null) cameraAdapter.FaceYaw(transform.eulerAngles.y);
        }

        /// <summary>
        /// プレイヤーの Transform を置く位置を球で、足元の目安を下向きの線で描く。
        /// </summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, GIZMO_RADIUS);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.down * GIZMO_LINE_LENGTH);
        }
    }
}
