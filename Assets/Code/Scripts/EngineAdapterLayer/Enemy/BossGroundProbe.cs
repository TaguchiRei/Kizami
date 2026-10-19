using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ボスが地面の高さを調べるレイを撃つ。ボス自身のコライダーは地面に数えない。
    /// ボスのボクセルは地面と同じ Default レイヤーにあるので、レイヤーでは分けられない。
    /// </summary>
    public static class BossGroundProbe
    {
        /// <summary> 1回のレイで受け取る当たりの数。ボスのボクセルのチャンクが並ぶので多めに取る </summary>
        private const int BUFFER_SIZE = 64;

        private static readonly RaycastHit[] _buffer = new RaycastHit[BUFFER_SIZE];

        /// <summary>
        /// origin から真下へレイを撃ち、boss の子孫でないコライダーのうち最も近いものに当たった位置を返す。
        /// </summary>
        /// <param name="boss">地面に数えないコライダーを持つボスの根</param>
        /// <param name="origin">レイの始点（ワールド）</param>
        /// <param name="distance">レイの長さ（m）</param>
        /// <param name="layers">地面のレイヤー</param>
        /// <param name="point">当たった位置（ワールド）。当たらなければ origin</param>
        /// <returns>地面に当たったか</returns>
        public static bool TryGetGround(Transform boss, Vector3 origin, float distance, LayerMask layers, out Vector3 point)
        {
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, _buffer, distance, layers,
                QueryTriggerInteraction.Ignore);

            var nearest = float.MaxValue;
            point = origin;
            var found = false;
            for (var i = 0; i < count; i++)
            {
                var hit = _buffer[i];
                if (hit.distance >= nearest || hit.collider.transform.IsChildOf(boss)) continue;

                nearest = hit.distance;
                point = hit.point;
                found = true;
            }

            return found;
        }
    }
}
