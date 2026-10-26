using UnityEngine;
using UnityEngine.Audio;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 効果音と BGM を鳴らす Adapter。常駐シーンへ置く。
    /// 効果音は事前に作った AudioSource を使い回し、全部使い中なら鳴らさない。同じ種類は最小間隔より短く続けて鳴らさない。
    /// 音量は AudioMixer のグループ経由で設定に従う。クリップが空なら何もしない。
    /// </summary>
    public sealed class SoundPlayer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("効果音の出力先グループ")]
        private AudioMixerGroup _seGroup;

        [SerializeField]
        [Tooltip("BGM の出力先グループ")]
        private AudioMixerGroup _bgmGroup;

        [SerializeField]
        [Tooltip("ループ再生する BGM。空なら鳴らさない")]
        private AudioClip _bgmClip;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("BGM の音量。設定の音量とは別の、素材ごとの調整")]
        private float _bgmVolume = 1f;

        [SerializeField]
        [Tooltip("ガラスが割れる音。複数あれば毎回ランダムに選ぶ")]
        private AudioClip[] _glassBreakClips;

        [SerializeField]
        [Tooltip("装甲を切る音。複数あれば毎回ランダムに選ぶ")]
        private AudioClip[] _armorCutClips;

        [SerializeField]
        [Tooltip("ボクセルを攻撃した音。複数あれば毎回ランダムに選ぶ")]
        private AudioClip[] _voxelHitClips;

        [SerializeField]
        [Tooltip("ボクセルが崩れる音。複数あれば毎回ランダムに選ぶ")]
        private AudioClip[] _voxelCollapseClips;

        [SerializeField]
        [Tooltip("ボスの攻撃の予兆の音。複数あればすべて重ねて鳴らす")]
        private AudioClip[] _bossWarningClips;

        [SerializeField, Min(1)]
        [Tooltip("同時に鳴らせる効果音の数")]
        private int _voiceCount = 16;

        [SerializeField, Min(0f)]
        [Tooltip("同じ種類の効果音を続けて鳴らす最小の間隔（秒）。大量の破壊で同じ音が重なるのを間引く")]
        private float _minInterval = 0.05f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("効果音の立体感。0 で位置によらず一定、1 で距離に応じて小さくなる")]
        private float _spatialBlend = 0.6f;

        [SerializeField, Min(1f)]
        [Tooltip("効果音が聞こえなくなる距離（m）")]
        private float _maxDistance = 80f;

        private AudioSource[] _voices;
        private float[] _lastPlayedTimes;

        /// <summary> 常駐シーンにある SoundPlayer。置かれていなければ null </summary>
        public static SoundPlayer Instance { get; private set; }

        /// <summary>
        /// 効果音を position で鳴らす。SoundPlayer が無い、クリップが空、間引き対象、全部使い中のいずれかなら何もしない。
        /// </summary>
        /// <param name="kind">効果音の種類</param>
        /// <param name="position">鳴らす位置（ワールド空間）</param>
        public static void Play(SoundEffectKind kind, Vector3 position)
        {
            if (Instance != null) Instance.PlayEffect(kind, position);
        }

        /// <summary>
        /// 種類 kind のクリップをすべて同時に position で鳴らす。ランダムに 1 つ選ぶ Play と違い、重ねて鳴らす。
        /// </summary>
        /// <param name="kind">効果音の種類</param>
        /// <param name="position">鳴らす位置（ワールド空間）</param>
        public static void PlayLayered(SoundEffectKind kind, Vector3 position)
        {
            if (Instance != null) Instance.PlayAllClips(kind, position);
        }

        private void Awake()
        {
            Instance = this;
            _lastPlayedTimes = new float[System.Enum.GetValues(typeof(SoundEffectKind)).Length];
            for (var i = 0; i < _lastPlayedTimes.Length; i++)
            {
                _lastPlayedTimes[i] = float.NegativeInfinity;
            }

            _voices = new AudioSource[_voiceCount];
            for (var i = 0; i < _voices.Length; i++)
            {
                var voiceObject = new GameObject($"SeVoice_{i:00}");
                voiceObject.transform.SetParent(transform, false);
                var voice = voiceObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.outputAudioMixerGroup = _seGroup;
                voice.spatialBlend = _spatialBlend;
                voice.rolloffMode = AudioRolloffMode.Linear;
                voice.maxDistance = _maxDistance;
                _voices[i] = voice;
            }
        }

        private void Start()
        {
            if (_bgmClip == null) return;

            var bgm = gameObject.AddComponent<AudioSource>();
            bgm.clip = _bgmClip;
            bgm.loop = true;
            bgm.volume = _bgmVolume;
            bgm.spatialBlend = 0f;
            bgm.outputAudioMixerGroup = _bgmGroup;
            bgm.Play();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void PlayEffect(SoundEffectKind kind, Vector3 position)
        {
            var clips = GetClips(kind);
            if (clips == null || clips.Length == 0) return;

            var now = Time.unscaledTime;
            if (now - _lastPlayedTimes[(int)kind] < _minInterval) return;

            var clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;

            foreach (var voice in _voices)
            {
                if (voice.isPlaying) continue;

                _lastPlayedTimes[(int)kind] = now;
                voice.transform.position = position;
                voice.clip = clip;
                voice.Play();
                return;
            }
        }

        private void PlayAllClips(SoundEffectKind kind, Vector3 position)
        {
            var clips = GetClips(kind);
            if (clips == null) return;

            var now = Time.unscaledTime;
            if (now - _lastPlayedTimes[(int)kind] < _minInterval) return;

            _lastPlayedTimes[(int)kind] = now;
            foreach (var clip in clips)
            {
                if (clip == null) continue;

                foreach (var voice in _voices)
                {
                    if (voice.isPlaying) continue;

                    voice.transform.position = position;
                    voice.clip = clip;
                    voice.Play();
                    break;
                }
            }
        }

        private AudioClip[] GetClips(SoundEffectKind kind)
        {
            return kind switch
            {
                SoundEffectKind.GlassBreak => _glassBreakClips,
                SoundEffectKind.ArmorCut => _armorCutClips,
                SoundEffectKind.VoxelHit => _voxelHitClips,
                SoundEffectKind.VoxelCollapse => _voxelCollapseClips,
                SoundEffectKind.BossWarning => _bossWarningClips,
                _ => null
            };
        }
    }
}
