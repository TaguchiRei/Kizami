using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 操作に関わる設定（ダッシュ入力の受け付け方・切断面の回転角度・視点操作の感度）を保持するゲームステート。シーンを跨いで保たれる。
    /// 感度は基準を 1.0 とする倍率で、生の視点入力の単位が経路ごとに違う（PC / スマホはスクリーン座標の delta、VR はスティックの -1〜1）ため、基準スケールは各入力経路・適用側が定数として持つ。
    /// </summary>
    [RegisterBoard(typeof(AppBoard))]
    public sealed class OperationSettingState : GameStateBase, IOperationSettingState
    {
        /// <summary> 感度倍率として受け付ける下限 </summary>
        public const float MIN_SENSITIVITY = 0.01f;

        /// <summary> 感度倍率として受け付ける上限 </summary>
        public const float MAX_SENSITIVITY = 10f;

        public SprintInputMode SprintInputMode { get; private set; } = SprintInputMode.Hold;

        public float CutRotateStepAngle { get; private set; } = 15f;

        public float HorizontalSensitivity { get; private set; } = 1f;

        public float VerticalSensitivity { get; private set; } = 1f;

        public override string GetLog()
        {
            return $"SprintInputMode: {SprintInputMode}  \nCutRotateStepAngle: {CutRotateStepAngle}  \n" +
                   $"HorizontalSensitivity: {HorizontalSensitivity}  \nVerticalSensitivity: {VerticalSensitivity}";
        }

        /// <summary>
        /// ダッシュ入力の受け付け方を設定する。
        /// </summary>
        /// <param name="mode">ダッシュ入力の受け付け方</param>
        public void SetSprintInputMode(SprintInputMode mode)
        {
            SprintInputMode = mode;
        }

        /// <summary>
        /// 切断面の回転入力 1 回あたりの回転角度を設定する。
        /// </summary>
        /// <param name="angle">回転角度（度）</param>
        public void SetCutRotateStepAngle(float angle)
        {
            CutRotateStepAngle = angle;
        }

        /// <summary>
        /// 左右の視点操作の感度倍率を設定する。値は MIN_SENSITIVITY 〜 MAX_SENSITIVITY にクランプされる。
        /// </summary>
        /// <param name="sensitivity">感度倍率</param>
        public void SetHorizontalSensitivity(float sensitivity)
        {
            HorizontalSensitivity = Mathf.Clamp(sensitivity, MIN_SENSITIVITY, MAX_SENSITIVITY);
        }

        /// <summary>
        /// 上下の視点操作の感度倍率を設定する。値は MIN_SENSITIVITY 〜 MAX_SENSITIVITY にクランプされる。
        /// </summary>
        /// <param name="sensitivity">感度倍率</param>
        public void SetVerticalSensitivity(float sensitivity)
        {
            VerticalSensitivity = Mathf.Clamp(sensitivity, MIN_SENSITIVITY, MAX_SENSITIVITY);
        }
    }

    /// <summary>
    /// 操作に関わる設定の読み取り面。
    /// </summary>
    public interface IOperationSettingState : IStateGetter
    {
        /// <summary> ダッシュ入力の受け付け方 </summary>
        SprintInputMode SprintInputMode { get; }

        /// <summary> 切断面の回転入力（ホイール 1 段）1 回あたりの回転角度（度） </summary>
        float CutRotateStepAngle { get; }

        /// <summary> 左右の視点操作の感度倍率 </summary>
        float HorizontalSensitivity { get; }

        /// <summary> 上下の視点操作の感度倍率。VR では適用側が上下方向の入力を捨てる </summary>
        float VerticalSensitivity { get; }
    }

    /// <summary>
    /// ダッシュ入力の受け付け方。
    /// </summary>
    public enum SprintInputMode
    {
        /// <summary> 押している間だけダッシュする </summary>
        Hold,

        /// <summary> 押すたびにダッシュの有無を切り替える。移動入力が 0 になると解除する </summary>
        Toggle
    }
}
