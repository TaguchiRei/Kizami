using Kizami.BlackBoard;
using UnityEngine;
using UnityEngine.Audio;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.Initialization;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 設定の音量（OperationSettingState の 0〜100）を AudioMixer の Exposed Parameter へ反映する Adapter。常駐シーンへ置く。
    /// Mixer には MasterVolume・BgmVolume・SeVolume の 3 つの Exposed Parameter（dB）が必要。値が変わったときだけ反映する。
    /// </summary>
    public sealed class AudioVolumeAdapter : InitializableMonoBehaviour
    {
        private const string MASTER_PARAMETER = "MasterVolume";
        private const string BGM_PARAMETER = "BgmVolume";
        private const string SE_PARAMETER = "SeVolume";
        private const float SILENT_DECIBEL = -80f;

        private AudioMixer _mixer;
        private IOperationSettingState _state;
        private int _appliedMaster = -1;
        private int _appliedBgm = -1;
        private int _appliedSe = -1;

        /// <summary>
        /// 設定の State を取得して、今の音量を反映する。OperationSettingState の登録より後に呼ぶ。
        /// </summary>
        /// <param name="blackBoard">IOperationSettingState の取得元</param>
        /// <param name="mixer">音量を反映する AudioMixer</param>
        public void Initialize(IBlackBoard blackBoard, AudioMixer mixer)
        {
            _mixer = mixer;
            if (!blackBoard.TryGetGameState<AppBoard, IOperationSettingState>(out _state, this)) return;

            base.Initialize();
            Apply();
        }

        private static float ToDecibel(int volume)
        {
            return volume <= 0 ? SILENT_DECIBEL : Mathf.Max(SILENT_DECIBEL, 20f * Mathf.Log10(volume / (float)OperationSettingState.MAX_VOLUME));
        }

        private void Update()
        {
            if (!Initialized) return;

            Apply();
        }

        private void Apply()
        {
            if (_mixer == null) return;

            if (_appliedMaster != _state.MasterVolume)
            {
                _appliedMaster = _state.MasterVolume;
                _mixer.SetFloat(MASTER_PARAMETER, ToDecibel(_appliedMaster));
            }

            if (_appliedBgm != _state.BgmVolume)
            {
                _appliedBgm = _state.BgmVolume;
                _mixer.SetFloat(BGM_PARAMETER, ToDecibel(_appliedBgm));
            }

            if (_appliedSe != _state.SeVolume)
            {
                _appliedSe = _state.SeVolume;
                _mixer.SetFloat(SE_PARAMETER, ToDecibel(_appliedSe));
            }
        }
    }
}
