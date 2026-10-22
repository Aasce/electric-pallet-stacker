using System.Collections.Generic;
using ElectricPalletStackers.Localization;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        [SerializeField] private AudioCueLibrary _library;
        [SerializeField, Min(0)] private int _prewarmCount = 12;
        [SerializeField, Min(1)] private int _maxPoolSize = 64;
        [SerializeField] private bool _persistAcrossScenes = true;
        [SerializeField] private bool _logMissingKeys = true;

        [Header("Localized Voice")]
        [SerializeField] private VoicePlaybackSystem _voice = new();

        private readonly Stack<PooledAudioVoice> _available = new();
        private readonly List<PooledAudioVoice> _active = new();
        private readonly Dictionary<int, PooledAudioVoice> _voicesById = new();
        private int _nextVoiceId = 1;

        public static AudioManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (_library == null)
                Debug.LogError("AudioManager requires an AudioCueLibrary assigned in the Inspector.", this);
            if (_persistAcrossScenes) DontDestroyOnLoad(gameObject);

            _voice ??= new VoicePlaybackSystem();
            _voice.Initialize(this);

            _maxPoolSize = Mathf.Max(1, _maxPoolSize);
            int count = Mathf.Min(Mathf.Max(0, _prewarmCount), _maxPoolSize);
            for (int index = 0; index < count; index++)
                _available.Push(CreateVoice());
        }

        private void Update()
        {
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                PooledAudioVoice voice = _active[index];
                if (voice == null || voice.HasFinished() ||
                    (voice.IsLooping && voice.HasLostFollowTarget))
                {
                    ReleaseAt(index);
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public AudioHandle Play(string key, AudioPlayOptions options = default)
        {
            return PlayInternal(key, transform.position, null, Vector3.zero, options);
        }

        public AudioHandle Play(string key, Vector3 position, AudioPlayOptions options = default)
        {
            return PlayInternal(key, position, null, Vector3.zero, options);
        }

        public AudioHandle Play(
            string key,
            Transform followTarget,
            AudioPlayOptions options = default,
            Vector3 worldOffset = default)
        {
            Vector3 position = followTarget != null ? followTarget.position + worldOffset : transform.position;
            return PlayInternal(key, position, followTarget, worldOffset, options);
        }

        public VoicePlaybackHandle PlayVoice(VoiceCueReference cue)
        {
            return _voice.Play(cue);
        }

        public void SetVoiceLanguage(LanguageCode language)
        {
            _voice.SetLanguage(language);
        }

        public void StopVoice()
        {
            _voice.Stop();
        }

        public void StopAll()
        {
            _voice.Stop();
            for (int index = _active.Count - 1; index >= 0; index--)
                ReleaseAt(index);
        }

        internal bool IsVoicePlaying(uint generation)
        {
            return _voice.IsPlaying(generation);
        }

        internal void StopVoice(uint generation)
        {
            _voice.Stop(generation);
        }

        internal bool IsHandleValid(int voiceId, uint generation)
        {
            return TryGetActiveVoice(voiceId, generation, out _);
        }

        internal bool IsPlaying(int voiceId, uint generation)
        {
            return TryGetActiveVoice(voiceId, generation, out PooledAudioVoice voice) && voice.IsPlaying;
        }

        internal void Stop(int voiceId, uint generation)
        {
            if (!TryGetActiveVoice(voiceId, generation, out PooledAudioVoice voice)) return;
            int index = _active.IndexOf(voice);
            if (index >= 0) ReleaseAt(index);
        }

        internal void SetVolume(int voiceId, uint generation, float volume)
        {
            if (TryGetActiveVoice(voiceId, generation, out PooledAudioVoice voice))
                voice.SetVolume(volume);
        }

        private AudioHandle PlayInternal(
            string key,
            Vector3 position,
            Transform followTarget,
            Vector3 followOffset,
            AudioPlayOptions options)
        {
            if (_library == null || !_library.TryGet(key, out AudioCueConfig config) ||
                config == null || config.Clip == null)
            {
                if (_logMissingKeys)
                    Debug.LogWarning($"Audio key '{key}' is missing or has no clip.", this);
                return default;
            }

            PooledAudioVoice voice = AcquireVoice();
            if (voice == null) return default;

            voice.Play(config, options, position, followTarget, followOffset);
            _active.Add(voice);
            return new AudioHandle(this, voice.Id, voice.Generation);
        }

        private PooledAudioVoice AcquireVoice()
        {
            if (_available.Count > 0) return _available.Pop();
            if (_voicesById.Count < _maxPoolSize) return CreateVoice();
            if (_active.Count == 0) return null;

            int oldestIndex = -1;
            for (int index = 0; index < _active.Count; index++)
            {
                if (_active[index].IsLooping) continue;
                if (oldestIndex < 0 || _active[index].StartedAt < _active[oldestIndex].StartedAt)
                    oldestIndex = index;
            }

            // Preserve long-running ambience where possible, but never fail a play request.
            if (oldestIndex < 0) oldestIndex = 0;

            PooledAudioVoice oldest = _active[oldestIndex];
            _active.RemoveAt(oldestIndex);
            oldest.ResetVoice();
            return oldest;
        }

        private PooledAudioVoice CreateVoice()
        {
            GameObject voiceObject = new($"Pooled Audio Source {_nextVoiceId}");
            voiceObject.transform.SetParent(transform, false);
            PooledAudioVoice voice = voiceObject.AddComponent<PooledAudioVoice>();
            voice.Initialize(_nextVoiceId++);
            _voicesById.Add(voice.Id, voice);
            return voice;
        }

        private bool TryGetActiveVoice(int voiceId, uint generation, out PooledAudioVoice voice)
        {
            if (_voicesById.TryGetValue(voiceId, out voice) && voice != null &&
                voice.IsActive && voice.Generation == generation)
                return true;

            voice = null;
            return false;
        }

        private void ReleaseAt(int index)
        {
            PooledAudioVoice voice = _active[index];
            _active.RemoveAt(index);
            if (voice == null) return;
            voice.ResetVoice();
            _available.Push(voice);
        }
    }
}
