using System.Collections;
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
        [SerializeField, Min(0.1f)] private float _missingVoiceFallbackDuration = 10f;

        private AudioHandle _activeAudio;
        private VoicePlaybackHandle _activeVoice;
        private Coroutine _conversationCompletionRoutine;

        private void OnEnable()
        {
            if (_panel != null) _panel.StateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (_panel != null) _panel.StateChanged -= HandleStateChanged;
            CancelConversationCompletion();
            _activeAudio.Stop();
            _activeVoice.Stop();
        }

        private void HandleStateChanged(PhoneCallState previousState, PhoneCallState nextState)
        {
            CancelConversationCompletion();
            _activeAudio.Stop();
            _activeVoice.Stop();
            AudioManager manager = AudioManager.Instance;

            switch (nextState)
            {
                case PhoneCallState.Ringing:
                    if (manager != null)
                        _activeAudio = manager.Play(
                            PalletStackerAudioKeys.PhoneIncoming,
                            _followTarget,
                            new AudioPlayOptions { Loop = true });
                    break;
                case PhoneCallState.InCall:
                    if (manager != null)
                        _activeVoice = manager.PlayVoice(_conversationVoice);
                    _conversationCompletionRoutine = StartCoroutine(CompleteWhenVoiceFinishes());
                    break;
                case PhoneCallState.Ended:
                    manager?.Play(PalletStackerAudioKeys.PhoneHangup, _followTarget);
                    break;
            }
        }

        private IEnumerator CompleteWhenVoiceFinishes()
        {
            yield return null;

            if (_activeVoice.IsPlaying)
            {
                while (_activeVoice.IsPlaying) yield return null;
            }
            else
            {
                float elapsed = 0f;
                while (elapsed < _missingVoiceFallbackDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            _conversationCompletionRoutine = null;
            if (_panel != null && _panel.State == PhoneCallState.InCall)
                _panel.EndCall();
        }

        private void CancelConversationCompletion()
        {
            if (_conversationCompletionRoutine == null) return;
            StopCoroutine(_conversationCompletionRoutine);
            _conversationCompletionRoutine = null;
        }
    }
}
