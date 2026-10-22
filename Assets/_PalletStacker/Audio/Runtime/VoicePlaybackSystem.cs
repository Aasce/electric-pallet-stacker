using System;
using ElectricPalletStackers.Localization;
using UnityEngine;
using UnityEngine.Audio;

namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Localized narration subsystem owned and initialized by <see cref="AudioManager"/>.
    /// Voice lines intentionally use one non-spatial channel so new narration replaces
    /// the previous line instead of overlapping it.
    /// </summary>
    [Serializable]
    public sealed class VoicePlaybackSystem
    {
        [SerializeField] private VoiceClipLibrary _library;
        [SerializeField] private LanguageCode _defaultLanguage = LanguageCode.En;
        [SerializeField] private AudioMixerGroup _output;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] private bool _logMissingCues = true;

        private AudioManager _owner;
        private AudioSource _source;
        private LanguageCode _language;
        private uint _generation;

        public LanguageCode CurrentLanguage => _language;

        internal void Initialize(AudioManager owner)
        {
            _owner = owner;
            _language = _defaultLanguage;

            Transform existing = owner.transform.Find("Localized Voice Source");
            GameObject sourceObject;
            if (existing != null)
            {
                sourceObject = existing.gameObject;
            }
            else
            {
                sourceObject = new GameObject("Localized Voice Source");
                sourceObject.transform.SetParent(owner.transform, false);
            }

            _source = sourceObject.GetComponent<AudioSource>();
            if (_source == null) _source = sourceObject.AddComponent<AudioSource>();
            ConfigureSource();
        }

        public void SetLanguage(LanguageCode language)
        {
            _language = language;
        }

        internal VoicePlaybackHandle Play(VoiceCueReference cue)
        {
            if (_owner == null || _source == null) return default;
            if (_library == null || !_library.TryResolve(cue, _language, out AudioClip clip))
            {
                if (_logMissingCues && !cue.IsEmpty)
                    Debug.LogWarning(
                        $"Voice cue '{cue.Id}' has no clip for {_language} or English fallback.",
                        _owner);
                return default;
            }

            _generation++;
            _source.Stop();
            _source.clip = clip;
            _source.Play();
            return new VoicePlaybackHandle(_owner, _generation);
        }

        internal bool IsPlaying(uint generation)
        {
            return generation == _generation && _source != null && _source.isPlaying;
        }

        internal void Stop(uint generation)
        {
            if (generation != _generation) return;
            Stop();
        }

        internal void Stop()
        {
            if (_source == null) return;
            _source.Stop();
            _source.clip = null;
        }

        private void ConfigureSource()
        {
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.dopplerLevel = 0f;
            _source.priority = 32;
            _source.volume = Mathf.Clamp01(_volume);
            _source.outputAudioMixerGroup = _output;
        }
    }
}
