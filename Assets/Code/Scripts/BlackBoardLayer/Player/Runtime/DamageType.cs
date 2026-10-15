using System;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 攻撃が持つダメージの種類。1つの攻撃が複数の種類を同時に持つことがある。
    /// </summary>
    [Flags]
    public enum DamageType
    {
        /// <summary> 種類なし </summary>
        None = 0,

        /// <summary> 攻撃タイプ。剣の切断など </summary>
        Attack = 1 << 0,

        /// <summary> 破壊タイプ。ボクセルを削るスキルなど </summary>
        Destroy = 1 << 1,

        /// <summary> 粉砕タイプ。投げたかけらなど。装甲を一撃で壊す </summary>
        Shatter = 1 << 2
    }
}
