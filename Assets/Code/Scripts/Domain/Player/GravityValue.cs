using UnityEngine;

namespace Code.Scripts.Domain.Player
{
    /// <summary>
    /// 重力の設定（方向と強さ）を保持する値オブジェクト
    /// </summary>
    public readonly struct GravityValue
    {
        public Vector3 Direction { get; }
        public float Magnitude { get; }

        public GravityValue(Vector3 direction, float magnitude)
        {
            Direction = direction.normalized;
            Magnitude = magnitude;
        }

        public Vector3 GravityForce => Direction * Magnitude;
    }
}