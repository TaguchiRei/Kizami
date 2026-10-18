using System;
using Kizami.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 操作に関わる設定（OperationSettingState の項目）の保存データ。
    /// </summary>
    [Serializable]
    public sealed class OperationSettingSaveData
    {
        /// <summary> ダッシュ入力の受け付け方 </summary>
        public SprintInputMode SprintInputMode;

        /// <summary> つかむ入力の受け付け方 </summary>
        public GrabInputMode GrabInputMode;

        /// <summary> 切断面の回転入力1回あたりの回転角度（度） </summary>
        public float CutRotateStepAngle;

        /// <summary> 左右の視点操作の感度倍率 </summary>
        public float HorizontalSensitivity;

        /// <summary> 上下の視点操作の感度倍率 </summary>
        public float VerticalSensitivity;
    }
}
