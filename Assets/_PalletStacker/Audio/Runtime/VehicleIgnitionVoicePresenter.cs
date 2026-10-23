using ElectricPalletStackers.UI;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Bridges the ignition reminder's presentation event to a localized voice cue.
    /// The reminder owns situation timing; this component owns voice presentation only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleIgnitionVoicePresenter : MonoBehaviour
    {
        [SerializeField] private VehicleIgnitionReminder _reminder;
        [SerializeField] private VoiceCueReference _instruction;

        private VoicePlaybackHandle _activeVoice;

        private void OnEnable()
        {
            if (_reminder != null)
                _reminder.VisibilityChanged += HandleVisibilityChanged;
        }

        private void OnDisable()
        {
            if (_reminder != null)
                _reminder.VisibilityChanged -= HandleVisibilityChanged;

            _activeVoice.Stop();
        }

        private void HandleVisibilityChanged(bool visible)
        {
            _activeVoice.Stop();
            if (!visible) return;

            AudioManager manager = AudioManager.Instance;
            if (manager != null)
                _activeVoice = manager.PlayVoice(_instruction);
        }
    }
}
