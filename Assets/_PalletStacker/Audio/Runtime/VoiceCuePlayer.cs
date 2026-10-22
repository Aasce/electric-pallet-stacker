using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Generic UnityEvent target for future situations that only need to play one cue.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceCuePlayer : MonoBehaviour
    {
        [SerializeField] private VoiceCueReference _cue;

        private VoicePlaybackHandle _activeVoice;

        public void Play()
        {
            _activeVoice.Stop();
            AudioManager manager = AudioManager.Instance;
            if (manager != null) _activeVoice = manager.PlayVoice(_cue);
        }

        public void Stop()
        {
            _activeVoice.Stop();
        }

        private void OnDisable()
        {
            _activeVoice.Stop();
        }
    }
}
