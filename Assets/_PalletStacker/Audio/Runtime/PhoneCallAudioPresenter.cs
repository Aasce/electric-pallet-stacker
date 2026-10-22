using ElectricPalletStackers.UI;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    [DisallowMultipleComponent]
    public sealed class PhoneCallAudioPresenter : MonoBehaviour
    {
        [SerializeField] private PhoneCallPanel _panel;
        [SerializeField] private Transform _followTarget;
        [SerializeField] private VoiceCueReference _conversationVoice;

        private AudioHandle _activeAudio;
        private VoicePlaybackHandle _activeVoice;

        private void OnEnable()
        {
            if (_panel != null) _panel.StateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (_panel != null) _panel.StateChanged -= HandleStateChanged;
            _activeAudio.Stop();
            _activeVoice.Stop();
        }

        private void HandleStateChanged(PhoneCallState previousState, PhoneCallState nextState)
        {
            _activeAudio.Stop();
            _activeVoice.Stop();
            AudioManager manager = AudioManager.Instance;
            if (manager == null) return;

            switch (nextState)
            {
                case PhoneCallState.Ringing:
                    _activeAudio = manager.Play(
                        PalletStackerAudioKeys.PhoneIncoming,
                        _followTarget,
                        new AudioPlayOptions { Loop = true });
                    break;
                case PhoneCallState.InCall:
                    _activeVoice = manager.PlayVoice(_conversationVoice);
                    break;
                case PhoneCallState.Ended:
                    manager.Play(PalletStackerAudioKeys.PhoneHangup, _followTarget);
                    break;
            }
        }
    }
}
