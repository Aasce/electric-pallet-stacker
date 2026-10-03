using ElectricPalletStackers.UI;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    [DisallowMultipleComponent]
    public sealed class PhoneCallAudioPresenter : MonoBehaviour
    {
        [SerializeField] private PhoneCallPanel _panel;
        [SerializeField] private Transform _followTarget;

        private AudioHandle _activeAudio;

        private void Awake()
        {
            if (_panel == null) _panel = GetComponent<PhoneCallPanel>();
            if (_followTarget == null) _followTarget = transform;
        }

        private void OnEnable()
        {
            if (_panel != null) _panel.StateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (_panel != null) _panel.StateChanged -= HandleStateChanged;
            _activeAudio.Stop();
        }

        private void HandleStateChanged(PhoneCallState previousState, PhoneCallState nextState)
        {
            _activeAudio.Stop();
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
                    _activeAudio = manager.Play(
                        PalletStackerAudioKeys.PhoneConversation,
                        _followTarget);
                    break;
                case PhoneCallState.Hidden when previousState != PhoneCallState.Hidden:
                    manager.Play(PalletStackerAudioKeys.PhoneHangup, _followTarget);
                    break;
            }
        }
    }
}
