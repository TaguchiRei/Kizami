using Kizami.EngineAdapter.Voxel;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// ビームの先端が当たったボクセルを、射線に沿って少しずつ溶かす。フィニッシャーの極太ビームと、ボスの頭部のレーザーが使う。
    /// 当たった物の親に VoxelPiece がなければ（メッシュのままの建物、装甲、地形）、溶かさない。
    /// </summary>
    public static class VoxelBeamMelt
    {
        /// <summary>
        /// 当たった面から射線に沿って depth だけ奥へ、半径 radius の球を動かして溶かす。
        /// 球の中心は、面から半径だけ手前から動かすので、1 回で面から奥へ進む深さが depth に収まる。
        /// </summary>
        /// <param name="hitCollider">ビームが最初に当たったコライダー。null なら何もしない</param>
        /// <param name="surface">ビームが最初に当たった面の位置</param>
        /// <param name="direction">ビームの向き（単位ベクトル）</param>
        /// <param name="radius">穴の半径（m）</param>
        /// <param name="depth">1 回で奥へ進む深さ（m）</param>
        /// <param name="falloff">1m あたりに減る強さ</param>
        /// <returns>ボクセルに当てたか</returns>
        public static bool Melt(Collider hitCollider, Vector3 surface, Vector3 direction, float radius, float depth,
            float falloff)
        {
            if (hitCollider == null) return false;

            var piece = hitCollider.GetComponentInParent<VoxelPiece>();
            if (piece == null) return false;

            var start = surface - direction * radius;
            var end = start + direction * depth;
            var reach = radius + VoxelThermalSettings.NORMAL_MELTING_POINT / falloff;
            piece.ApplyStrike(new CapsuleShape(start, end, reach), falloff, Space.World);
            return true;
        }
    }
}
