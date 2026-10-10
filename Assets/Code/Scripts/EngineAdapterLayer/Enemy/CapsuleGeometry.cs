using Unity.Mathematics;
using UnityEngine;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 敵の攻撃とプレイヤーのカプセルとの当たりを調べる、形の計算。弾（EnemyShooter）・バリア（EnemyBarriers）・ビーム（EnemyFinisherAttack）が使う。
    /// 線分どうしの距離は Burst の Job からも呼ぶ。
    /// </summary>
    public static class CapsuleGeometry
    {
        /// <summary>
        /// CapsuleCollider の軸の両端（両端の球の中心、ワールド座標）と半径を求める。カプセルは縦向き（Y 軸）とする。
        /// </summary>
        public static void GetAxis(CapsuleCollider capsule, out float3 start, out float3 end, out float radius)
        {
            var capsuleTransform = capsule.transform;
            var scale = capsuleTransform.lossyScale;
            radius = capsule.radius * math.max(math.abs(scale.x), math.abs(scale.z));
            var halfAxis = math.max(capsule.height * 0.5f * math.abs(scale.y) - radius, 0f);
            var center = (float3)capsuleTransform.TransformPoint(capsule.center);
            var up = (float3)capsuleTransform.up;
            start = center - up * halfAxis;
            end = center + up * halfAxis;
        }

        /// <summary>
        /// 線分 p1-q1 と線分 p2-q2 の最も近い点どうしの距離の 2 乗と、線分 p1-q1 の上の最も近い点の割合（0〜1）を求める。
        /// </summary>
        public static float GetSegmentDistanceSq(float3 p1, float3 q1, float3 p2, float3 q2, out float s)
        {
            const float EPSILON = 1e-8f;
            var d1 = q1 - p1;
            var d2 = q2 - p2;
            var r = p1 - p2;
            var a = math.dot(d1, d1);
            var e = math.dot(d2, d2);
            var f = math.dot(d2, r);
            float t;

            if (a <= EPSILON && e <= EPSILON)
            {
                s = 0f;
                t = 0f;
            }
            else if (a <= EPSILON)
            {
                s = 0f;
                t = math.saturate(f / e);
            }
            else
            {
                var c = math.dot(d1, r);
                if (e <= EPSILON)
                {
                    t = 0f;
                    s = math.saturate(-c / a);
                }
                else
                {
                    var b = math.dot(d1, d2);
                    var denominator = a * e - b * b;
                    s = denominator > EPSILON ? math.saturate((b * f - c * e) / denominator) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = math.saturate(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = math.saturate((b - c) / a);
                    }
                }
            }

            return math.distancesq(p1 + d1 * s, p2 + d2 * t);
        }
    }
}
