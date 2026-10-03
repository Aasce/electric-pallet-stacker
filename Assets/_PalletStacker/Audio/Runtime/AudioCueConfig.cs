using System;
using UnityEngine;
using UnityEngine.Audio;

namespace ElectricPalletStackers.Audio
{
    [Serializable]
    public sealed class AudioCueConfig
    {
        [SerializeField] private AudioClip _clip;
        [SerializeField] private AudioMixerGroup _output;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] private Vector2 _pitchRange = Vector2.one;
        [SerializeField] private bool _loop;

        [Header("Spatial")]
        [SerializeField, Range(0f, 1f)] private float _spatialBlend = 1f;
        [SerializeField, Min(0f)] private float _minDistance = 1f;
        [SerializeField, Min(0.01f)] private float _maxDistance = 25f;
        [SerializeField] private AudioRolloffMode _rolloffMode = AudioRolloffMode.Logarithmic;
        [SerializeField, Range(0f, 5f)] private float _dopplerLevel;
        [SerializeField, Range(0f, 360f)] private float _spread;
        [SerializeField] private bool _spatialize;
        [SerializeField, Range(0, 256)] private int _priority = 128;

        public AudioClip Clip => _clip;
        public bool Loop => _loop;

        internal void ApplyTo(AudioSource source, AudioPlayOptions options)
        {
            source.clip = _clip;
            source.outputAudioMixerGroup = options.OverrideOutput ? options.Output : _output;
            source.volume = Mathf.Clamp01(options.Volume ?? _volume);
            source.pitch = options.Pitch ?? UnityEngine.Random.Range(
                Mathf.Min(_pitchRange.x, _pitchRange.y),
                Mathf.Max(_pitchRange.x, _pitchRange.y));
            source.loop = options.Loop ?? _loop;
            source.spatialBlend = Mathf.Clamp01(options.SpatialBlend ?? _spatialBlend);
            source.minDistance = Mathf.Max(0f, _minDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 0.01f, _maxDistance);
            source.rolloffMode = _rolloffMode;
            source.dopplerLevel = _dopplerLevel;
            source.spread = _spread;
            source.spatialize = _spatialize;
            source.priority = Mathf.Clamp(_priority, 0, 256);
            source.playOnAwake = false;
        }
    }
}
