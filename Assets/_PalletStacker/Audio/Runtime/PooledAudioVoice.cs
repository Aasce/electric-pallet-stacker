using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    [DisallowMultipleComponent]
    internal sealed class PooledAudioVoice : MonoBehaviour
    {
        private AudioSource _source;
        private AudioSourceFollower _follower;
        private int _startedFrame;

        public int Id { get; private set; }
        public uint Generation { get; private set; }
        public bool IsActive { get; private set; }
        public float StartedAt { get; private set; }
        public bool IsPlaying => IsActive && _source != null && _source.isPlaying;
        public bool HasLostFollowTarget => _follower != null && _follower.HasLostTarget;
        public bool IsLooping => _source != null && _source.loop;

        public void Initialize(int id)
        {
            Id = id;
            _source = GetComponent<AudioSource>();
            if (_source == null) _source = gameObject.AddComponent<AudioSource>();
            _follower = GetComponent<AudioSourceFollower>();
            if (_follower == null) _follower = gameObject.AddComponent<AudioSourceFollower>();
            ResetVoice();
        }

        public void Play(
            AudioCueConfig config,
            AudioPlayOptions options,
            Vector3 position,
            Transform followTarget,
            Vector3 followOffset)
        {
            Generation++;
            IsActive = true;
            StartedAt = Time.unscaledTime;
            _startedFrame = Time.frameCount;
            transform.position = position;
            config.ApplyTo(_source, options);

            if (followTarget != null) _follower.Follow(followTarget, followOffset);
            else _follower.Clear();

            gameObject.SetActive(true);
            _source.Play();
        }

        public bool HasFinished()
        {
            if (!IsActive || _source == null || _source.loop) return false;
            return Time.frameCount > _startedFrame && !_source.isPlaying;
        }

        public void SetVolume(float volume)
        {
            if (_source != null) _source.volume = Mathf.Clamp01(volume);
        }

        public void ResetVoice()
        {
            if (_source != null)
            {
                _source.Stop();
                _source.clip = null;
                _source.loop = false;
                _source.outputAudioMixerGroup = null;
            }

            if (_follower != null) _follower.Clear();
            IsActive = false;
            gameObject.SetActive(false);
        }
    }
}
