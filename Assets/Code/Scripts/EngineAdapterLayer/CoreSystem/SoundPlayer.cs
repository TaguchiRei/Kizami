using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

namespace Kizami.EngineAdapter
{
    /// <summary>
    /// 効果音と BGM を鳴らす Adapter。常駐シーンへ置く。
    /// 効果音は事前に作った AudioSource を使い回し、全部使い中なら鳴らさない。同じ種類は種類ごとの最小間隔より短く続けて鳴らさない。
    /// 音量は AudioMixer のグループ経由で設定に従い、種類ごとの音量とピッチの幅は登録で調整する。クリップが空なら何もしない。
    /// </summary>
    public sealed class SoundPlayer : MonoBehaviour
    {
        private const float DEFAULT_MIN_INTERVAL = 0.05f;

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
        [Tooltip("効果音の登録。種類ごとに 1 つ。登録がない種類は鳴らさない")]
        private List<Entry> _effects = new();

        [SerializeField, Min(1)]
        [Tooltip("同時に鳴らせる効果音の数")]
        private int _voiceCount = 16;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("効果音の立体感。0 で位置によらず一定、1 で距離に応じて小さくなる")]
        private float _spatialBlend = 0.6f;

        [SerializeField, Min(1f)]
        [Tooltip("効果音が聞こえなくなる距離（m）")]
        private float _maxDistance = 80f;

        private readonly Dictionary<SoundEffectKind, Entry> _entries = new();
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

        /// <summary>
        /// 位置によらず一定の音量で効果音を鳴らす。UI の音に使う。鳴らせない場合は Play と同じ。
        /// </summary>
        /// <param name="kind">効果音の種類</param>
        public static void PlayUi(SoundEffectKind kind)
        {
            if (Instance != null) Instance.PlayEffect(kind, Vector3.zero, 0f);
        }

        private void Awake()
        {
            Instance = this;
            foreach (var entry in _effects)
            {
                _entries[entry.Kind] = entry;
            }

            _lastPlayedTimes = new float[Enum.GetValues(typeof(SoundEffectKind)).Length];
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
            PlayEffect(kind, position, _spatialBlend);
        }

        private void PlayEffect(SoundEffectKind kind, Vector3 position, float spatialBlend)
        {
            if (!_entries.TryGetValue(kind, out var entry) || entry.Clips == null || entry.Clips.Length == 0) return;

            var now = Time.unscaledTime;
            if (now - _lastPlayedTimes[(int)kind] < entry.MinInterval) return;

            var clip = entry.Clips[Random.Range(0, entry.Clips.Length)];
            if (clip == null) return;

            if (TryPlayOnFreeVoice(clip, entry, position, spatialBlend)) _lastPlayedTimes[(int)kind] = now;
        }

        private void PlayAllClips(SoundEffectKind kind, Vector3 position)
        {
            if (!_entries.TryGetValue(kind, out var entry) || entry.Clips == null) return;

            var now = Time.unscaledTime;
            if (now - _lastPlayedTimes[(int)kind] < entry.MinInterval) return;

            _lastPlayedTimes[(int)kind] = now;
            foreach (var clip in entry.Clips)
            {
                if (clip != null) TryPlayOnFreeVoice(clip, entry, position, _spatialBlend);
            }
        }

        /// <summary>
        /// 使われていない AudioSource にクリップを登録の音量とピッチで鳴らす。全部使い中なら鳴らさず false を返す。
        /// </summary>
        private bool TryPlayOnFreeVoice(AudioClip clip, Entry entry, Vector3 position, float spatialBlend)
        {
            foreach (var voice in _voices)
            {
                if (voice.isPlaying) continue;

                voice.transform.position = position;
                voice.spatialBlend = spatialBlend;
                voice.clip = clip;
                voice.volume = entry.Volume;
                voice.pitch = 1f + Random.Range(-entry.PitchRange, entry.PitchRange);
                voice.Play();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 効果音の種類ごとの登録。
        /// </summary>
        [Serializable]
        private sealed class Entry
        {
            [Tooltip("効果音の種類")]
            public SoundEffectKind Kind;

            [Tooltip("クリップ。Play は毎回ランダムに 1 つ選び、PlayLayered はすべて重ねて鳴らす")]
            public AudioClip[] Clips;

            [Range(0f, 1f)]
            [Tooltip("素材ごとの音量。設定の音量とは別の調整")]
            public float Volume = 1f;

            [Range(0f, 0.5f)]
            [Tooltip("鳴らすたびにピッチを 1 を中心にこの幅だけランダムにずらす。0 でずらさない")]
            public float PitchRange;

            [Min(0f)]
            [Tooltip("同じ種類を続けて鳴らす最小の間隔（秒）。大量の破壊で同じ音が重なるのを間引く")]
            public float MinInterval = DEFAULT_MIN_INTERVAL;
        }
    }
}
