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

        /// <summary> 音量を保存したか。false なら音量は初期値を使う（0 も有効な音量の為、値では見分けられない） </summary>
        public bool HasVolume;

        /// <summary> マスター音量（0〜100） </summary>
        public int MasterVolume;

        /// <summary> BGM の音量（0〜100） </summary>
        public int BgmVolume;

        /// <summary> SE の音量（0〜100） </summary>
        public int SeVolume;

        /// <summary> 視野角を保存したか。false なら視野角は初期値を使う（視野角を足す前の保存データでは値が 0 になる為） </summary>
        public bool HasFieldOfView;

        /// <summary> 歩行時の縦の視野角（度） </summary>
        public float MinFieldOfView;

        /// <summary> ダッシュ・壁走りで動いているときの縦の視野角（度） </summary>
        public float MaxFieldOfView;

        /// <summary> キーの割り当ての変更差分。変更がなければ空 </summary>
        public string BindingOverridesJson = string.Empty;
    }
}
