using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// プレイヤーが始まる位置の目印。ステージシーンへ1つ置く。置いていないステージでは、プレイヤーはインゲームのシーンに置いた位置から始まる。
    /// 位置はプレイヤーの Transform を置く位置で、向きは使わない。視点の向きは、カメラ側の初期値になる。
    /// </summary>
    public sealed class PlayerStartPoint : MonoBehaviour
    {
        /// <summary> 目印の球の半径（m） </summary>
        private const float GIZMO_RADIUS = 0.5f;

        /// <summary> 目印の球から地面へ引く線の長さ（m） </summary>
        private const float GIZMO_LINE_LENGTH = 1f;

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
