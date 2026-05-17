using UnityEngine;

namespace UsefulVr.Domain.Runtime.Player
{
    /// <summary>
    /// 重力接平面上における視線方向を保持する値オブジェクト
    /// </summary>
    public readonly struct LookDirectionValue
    {
        /// <summary>
        /// 接平面上の正規化済み方向ベクトル
        /// </summary>
        public Vector3 Value { get; }

        public LookDirectionValue(Vector3 value)
        {
            Value =
                value.sqrMagnitude > 0.001f
                    ? value.normalized
                    : Vector3.forward;
        }

        /// <summary>
        /// デフォルト値
        /// </summary>
        public static LookDirectionValue Forward =>
            new LookDirectionValue(Vector3.forward);
    }
}