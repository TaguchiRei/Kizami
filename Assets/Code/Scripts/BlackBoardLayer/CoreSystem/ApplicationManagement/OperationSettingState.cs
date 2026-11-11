using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.BlackBoard
{
    /// <summary>
    /// 操作に関わる設定（ダッシュ入力の受け付け方・つかむ入力の受け付け方・切断面の回転角度・視点操作の感度・音量・視野角）を保持するゲームステート。シーンを跨いで保たれる。
    /// 感度は基準を1.0とする倍率で、生の視点入力の単位が経路ごとに違う（PC / スマホはスクリーン座標の delta、VR はスティックの -1〜1）ため、基準スケールは各入力経路・適用側が定数として持つ。
    /// </summary>
    [RegisterBoard(typeof(AppBoard))]
    public sealed class OperationSettingState : GameStateBase, IOperationSettingState
    {
        /// <summary> 感度倍率として受け付ける下限 </summary>
        public const float MIN_SENSITIVITY = 0.01f;

        /// <summary> 感度倍率として受け付ける上限 </summary>
        public const float MAX_SENSITIVITY = 10f;

        /// <summary> 音量として受け付ける上限（下限は0） </summary>
        public const int MAX_VOLUME = 100;

        /// <summary> 音量の初期値 </summary>
        public const int DEFAULT_VOLUME = 80;

        /// <summary> 視野角（度）として受け付ける下限 </summary>
        public const float MIN_FIELD_OF_VIEW = 30f;

        /// <summary> 視野角（度）として受け付ける上限 </summary>
        public const float MAX_FIELD_OF_VIEW = 120f;

        /// <summary> 歩行時の視野角（度）の初期値 </summary>
        public const float DEFAULT_MIN_FIELD_OF_VIEW = 40f;

        /// <summary> ダッシュ・壁走りで動いているときの視野角（度）の初期値 </summary>
        public const float DEFAULT_MAX_FIELD_OF_VIEW = 48f;

        public SprintInputMode SprintInputMode { get; private set; } = SprintInputMode.Hold;

        public GrabInputMode GrabInputMode { get; private set; } = GrabInputMode.Toggle;

        public float CutRotateStepAngle { get; private set; } = 15f;

        public float HorizontalSensitivity { get; private set; } = 1f;

        public float VerticalSensitivity { get; private set; } = 1f;

        public int MasterVolume { get; private set; } = DEFAULT_VOLUME;

        public int BgmVolume { get; private set; } = DEFAULT_VOLUME;

        public int SeVolume { get; private set; } = DEFAULT_VOLUME;

        public float MinFieldOfView { get; private set; } = DEFAULT_MIN_FIELD_OF_VIEW;

        public float MaxFieldOfView { get; private set; } = DEFAULT_MAX_FIELD_OF_VIEW;

        public override string GetLog()
        {
            return $"SprintInputMode: {SprintInputMode}  \nGrabInputMode: {GrabInputMode}  \n" +
                   $"CutRotateStepAngle: {CutRotateStepAngle}  \n" +
                   $"HorizontalSensitivity: {HorizontalSensitivity}  \nVerticalSensitivity: {VerticalSensitivity}  \n" +
                   $"MasterVolume: {MasterVolume}  \nBgmVolume: {BgmVolume}  \nSeVolume: {SeVolume}  \n" +
                   $"MinFieldOfView: {MinFieldOfView}  \nMaxFieldOfView: {MaxFieldOfView}";
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
        /// つかむ入力の受け付け方を設定する。
        /// </summary>
        /// <param name="mode">つかむ入力の受け付け方</param>
        public void SetGrabInputMode(GrabInputMode mode)
        {
            GrabInputMode = mode;
        }

        /// <summary>
        /// 切断面の回転入力1回あたりの回転角度を設定する。
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

        /// <summary>
        /// マスター音量を設定する。値は 0 〜 MAX_VOLUME にクランプされる。
        /// </summary>
        /// <param name="volume">音量</param>
        public void SetMasterVolume(int volume)
        {
            MasterVolume = Mathf.Clamp(volume, 0, MAX_VOLUME);
        }

        /// <summary>
        /// BGM の音量を設定する。値は 0 〜 MAX_VOLUME にクランプされる。
        /// </summary>
        /// <param name="volume">音量</param>
        public void SetBgmVolume(int volume)
        {
            BgmVolume = Mathf.Clamp(volume, 0, MAX_VOLUME);
        }

        /// <summary>
        /// SE の音量を設定する。値は 0 〜 MAX_VOLUME にクランプされる。
        /// </summary>
        /// <param name="volume">音量</param>
        public void SetSeVolume(int volume)
        {
            SeVolume = Mathf.Clamp(volume, 0, MAX_VOLUME);
        }

        /// <summary>
        /// 歩行時と、止まっているときの視野角を設定する。値は MIN_FIELD_OF_VIEW 〜 MAX_FIELD_OF_VIEW にクランプされる。
        /// </summary>
        /// <param name="fieldOfView">縦の視野角（度）</param>
        public void SetMinFieldOfView(float fieldOfView)
        {
            MinFieldOfView = Mathf.Clamp(fieldOfView, MIN_FIELD_OF_VIEW, MAX_FIELD_OF_VIEW);
        }

        /// <summary>
        /// ダッシュ・壁走りで動いているときの視野角を設定する。値は MIN_FIELD_OF_VIEW 〜 MAX_FIELD_OF_VIEW にクランプされる。
        /// </summary>
        /// <param name="fieldOfView">縦の視野角（度）</param>
        public void SetMaxFieldOfView(float fieldOfView)
        {
            MaxFieldOfView = Mathf.Clamp(fieldOfView, MIN_FIELD_OF_VIEW, MAX_FIELD_OF_VIEW);
        }
    }

    /// <summary>
    /// 操作に関わる設定の読み取り面。
    /// </summary>
    public interface IOperationSettingState : IStateGetter
    {
        /// <summary> ダッシュ入力の受け付け方 </summary>
        SprintInputMode SprintInputMode { get; }

        /// <summary> つかむ入力の受け付け方 </summary>
        GrabInputMode GrabInputMode { get; }

        /// <summary> 切断面の回転入力（ホイール1段）1回あたりの回転角度（度） </summary>
        float CutRotateStepAngle { get; }

        /// <summary> 左右の視点操作の感度倍率 </summary>
        float HorizontalSensitivity { get; }

        /// <summary> 上下の視点操作の感度倍率。VR では適用側が上下方向の入力を捨てる </summary>
        float VerticalSensitivity { get; }

        /// <summary> マスター音量（0〜100） </summary>
        int MasterVolume { get; }

        /// <summary> BGM の音量（0〜100） </summary>
        int BgmVolume { get; }

        /// <summary> SE の音量（0〜100） </summary>
        int SeVolume { get; }

        /// <summary> 歩行時と、止まっているときの縦の視野角（度） </summary>
        float MinFieldOfView { get; }

        /// <summary> ダッシュ・壁走りで動いているときの縦の視野角（度）。MinFieldOfView より小さければ広げない </summary>
        float MaxFieldOfView { get; }
    }

    /// <summary>
    /// 操作に関わる設定の書き込み面。変更は保存される。
    /// </summary>
    public interface IOperationSettingController
    {
        /// <summary> キーの割り当ての変更差分（InputActionAsset の SaveBindingOverridesAsJson の文字列）。変更がなければ空 </summary>
        string BindingOverridesJson { get; }

        /// <summary> ダッシュ入力の受け付け方を変更して保存する </summary>
        void SetSprintInputMode(SprintInputMode mode);

        /// <summary> つかむ入力の受け付け方を変更して保存する </summary>
        void SetGrabInputMode(GrabInputMode mode);

        /// <summary> 切断面の回転入力1回あたりの回転角度（度）を変更して保存する </summary>
        void SetCutRotateStepAngle(float angle);

        /// <summary> 左右の視点操作の感度倍率を変更して保存する </summary>
        void SetHorizontalSensitivity(float sensitivity);

        /// <summary> 上下の視点操作の感度倍率を変更して保存する </summary>
        void SetVerticalSensitivity(float sensitivity);

        /// <summary> マスター音量（0〜100）を変更して保存する </summary>
        void SetMasterVolume(int volume);

        /// <summary> BGM の音量（0〜100）を変更して保存する </summary>
        void SetBgmVolume(int volume);

        /// <summary> SE の音量（0〜100）を変更して保存する </summary>
        void SetSeVolume(int volume);

        /// <summary> 歩行時の視野角（度）を変更して保存する </summary>
        void SetMinFieldOfView(float fieldOfView);

        /// <summary> ダッシュ・壁走りで動いているときの視野角（度）を変更して保存する </summary>
        void SetMaxFieldOfView(float fieldOfView);

        /// <summary> キーの割り当ての変更差分を保存する。初期値に戻すなら空文字 </summary>
        void SetBindingOverridesJson(string json);
    }

    /// <summary>
    /// ダッシュ入力の受け付け方。
    /// </summary>
    public enum SprintInputMode
    {
        /// <summary> 押している間だけダッシュする </summary>
        Hold,

        /// <summary> 押すたびにダッシュの有無を切り替える。移動入力が0になると解除する </summary>
        Toggle
    }

    /// <summary>
    /// かけらと文字をつかんで投げる入力の受け付け方。
    /// </summary>
    public enum GrabInputMode
    {
        /// <summary> 押している間つかみ、離すと投げる </summary>
        Hold,

        /// <summary> 1回押すとつかみ、もう1回押すと投げる </summary>
        Toggle
    }
}
