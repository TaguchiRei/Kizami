using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 操作に関わる設定を保持する OperationSettingState を生成し、値を書き込むユースケース。
    /// 設定は変更のたびに保存し、生成するときに読み込む。保存データがなければ初期値を使う。
    /// </summary>
    public sealed class OperationSettingService
    {
        private const string SAVE_FILE_NAME = "setting.json";

        private readonly OperationSettingState _state = new();
        private readonly JsonSaveStore<OperationSettingSaveData> _saveStore = new(SAVE_FILE_NAME);

        /// <param name="blackBoard">OperationSettingState の登録先</param>
        /// <param name="sprintInputMode">ダッシュ入力の受け付け方の初期値</param>
        /// <param name="grabInputMode">つかむ入力の受け付け方の初期値</param>
        /// <param name="cutRotateStepAngle">切断面の回転入力1回あたりの回転角度（度）の初期値</param>
        public OperationSettingService(IBlackBoard blackBoard, SprintInputMode sprintInputMode,
            GrabInputMode grabInputMode, float cutRotateStepAngle)
        {
            if (_saveStore.TryLoad(out var saveData))
            {
                _state.SetSprintInputMode(saveData.SprintInputMode);
                _state.SetGrabInputMode(saveData.GrabInputMode);
                _state.SetCutRotateStepAngle(saveData.CutRotateStepAngle);
                _state.SetHorizontalSensitivity(saveData.HorizontalSensitivity);
                _state.SetVerticalSensitivity(saveData.VerticalSensitivity);
            }
            else
            {
                _state.SetSprintInputMode(sprintInputMode);
                _state.SetGrabInputMode(grabInputMode);
                _state.SetCutRotateStepAngle(cutRotateStepAngle);
            }

            if (!blackBoard.TryGetBoard<AppBoard>(out var appBoard, this)) return;

            appBoard.RegisterGameState<IOperationSettingState>(_state);
        }

        /// <summary>
        /// つかむ入力の受け付け方を変更して保存する。
        /// </summary>
        /// <param name="mode">つかむ入力の受け付け方</param>
        public void SetGrabInputMode(GrabInputMode mode)
        {
            _state.SetGrabInputMode(mode);
            Save();
        }

        /// <summary>
        /// 左右の視点操作の感度倍率を変更して保存する。
        /// </summary>
        /// <param name="sensitivity">感度倍率</param>
        public void SetHorizontalSensitivity(float sensitivity)
        {
            _state.SetHorizontalSensitivity(sensitivity);
            Save();
        }

        /// <summary>
        /// 上下の視点操作の感度倍率を変更して保存する。
        /// </summary>
        /// <param name="sensitivity">感度倍率</param>
        public void SetVerticalSensitivity(float sensitivity)
        {
            _state.SetVerticalSensitivity(sensitivity);
            Save();
        }

        /// <summary>
        /// 今の設定を保存する。
        /// </summary>
        private void Save()
        {
            _saveStore.Save(new OperationSettingSaveData
            {
                SprintInputMode = _state.SprintInputMode,
                GrabInputMode = _state.GrabInputMode,
                CutRotateStepAngle = _state.CutRotateStepAngle,
                HorizontalSensitivity = _state.HorizontalSensitivity,
                VerticalSensitivity = _state.VerticalSensitivity,
            });
        }
    }
}
