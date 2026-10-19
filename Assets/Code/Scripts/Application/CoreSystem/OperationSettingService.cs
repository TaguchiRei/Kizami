using Kizami.BlackBoard;
using Kizami.External;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Kizami.Application
{
    /// <summary>
    /// 操作に関わる設定を保持する OperationSettingState を生成し、値を書き込むユースケース。
    /// 設定は変更のたびに保存し、生成するときに読み込む。保存データがなければ初期値を使う。
    /// 常駐シーンの Awake で生成して IOperationSettingController として配り、State の登録は Initialize で行う。
    /// </summary>
    public sealed class OperationSettingService : IOperationSettingController
    {
        private const string SAVE_FILE_NAME = "setting.json";

        private readonly OperationSettingState _state = new();
        private readonly JsonSaveStore<OperationSettingSaveData> _saveStore = new(SAVE_FILE_NAME);

        private string _bindingOverridesJson = string.Empty;

        /// <param name="sprintInputMode">ダッシュ入力の受け付け方の初期値</param>
        /// <param name="grabInputMode">つかむ入力の受け付け方の初期値</param>
        /// <param name="cutRotateStepAngle">切断面の回転入力1回あたりの回転角度（度）の初期値</param>
        public OperationSettingService(SprintInputMode sprintInputMode, GrabInputMode grabInputMode,
            float cutRotateStepAngle)
        {
            if (_saveStore.TryLoad(out var saveData))
            {
                _state.SetSprintInputMode(saveData.SprintInputMode);
                _state.SetGrabInputMode(saveData.GrabInputMode);
                _state.SetCutRotateStepAngle(saveData.CutRotateStepAngle);
                _state.SetHorizontalSensitivity(saveData.HorizontalSensitivity);
                _state.SetVerticalSensitivity(saveData.VerticalSensitivity);
                if (saveData.HasVolume)
                {
                    _state.SetMasterVolume(saveData.MasterVolume);
                    _state.SetBgmVolume(saveData.BgmVolume);
                    _state.SetSeVolume(saveData.SeVolume);
                }

                _bindingOverridesJson = saveData.BindingOverridesJson ?? string.Empty;
            }
            else
            {
                _state.SetSprintInputMode(sprintInputMode);
                _state.SetGrabInputMode(grabInputMode);
                _state.SetCutRotateStepAngle(cutRotateStepAngle);
            }
        }

        public string BindingOverridesJson => _bindingOverridesJson;

        /// <summary>
        /// OperationSettingState を AppBoard へ登録する。
        /// </summary>
        /// <param name="blackBoard">OperationSettingState の登録先</param>
        public void Initialize(IBlackBoard blackBoard)
        {
            if (!blackBoard.TryGetBoard<AppBoard>(out var appBoard, this)) return;

            appBoard.RegisterGameState<IOperationSettingState>(_state);
        }

        public void SetSprintInputMode(SprintInputMode mode)
        {
            _state.SetSprintInputMode(mode);
            Save();
        }

        public void SetGrabInputMode(GrabInputMode mode)
        {
            _state.SetGrabInputMode(mode);
            Save();
        }

        public void SetCutRotateStepAngle(float angle)
        {
            _state.SetCutRotateStepAngle(angle);
            Save();
        }

        public void SetHorizontalSensitivity(float sensitivity)
        {
            _state.SetHorizontalSensitivity(sensitivity);
            Save();
        }

        public void SetVerticalSensitivity(float sensitivity)
        {
            _state.SetVerticalSensitivity(sensitivity);
            Save();
        }

        public void SetMasterVolume(int volume)
        {
            _state.SetMasterVolume(volume);
            Save();
        }

        public void SetBgmVolume(int volume)
        {
            _state.SetBgmVolume(volume);
            Save();
        }

        public void SetSeVolume(int volume)
        {
            _state.SetSeVolume(volume);
            Save();
        }

        public void SetBindingOverridesJson(string json)
        {
            _bindingOverridesJson = json ?? string.Empty;
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
                HasVolume = true,
                MasterVolume = _state.MasterVolume,
                BgmVolume = _state.BgmVolume,
                SeVolume = _state.SeVolume,
                BindingOverridesJson = _bindingOverridesJson,
            });
        }
    }
}
