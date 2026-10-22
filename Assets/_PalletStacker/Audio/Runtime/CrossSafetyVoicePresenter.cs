using ElectricPalletStackers.Gameplay;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Voice adapter for the three authored cross-safety presentation events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrossSafetyVoicePresenter : MonoBehaviour
    {
        [SerializeField] private CrossSafetyPhaseController _phase;
        [SerializeField] private VoiceCueReference _approachInstruction;
        [SerializeField] private VoiceCueReference _safetyInstruction;
        [SerializeField] private VoiceCueReference _dangerWarning;

        private VoicePlaybackHandle _activeVoice;

        private void OnEnable()
        {
            if (_phase == null) return;
            _phase.ApproachInstructionPresented += HandleApproachInstruction;
            _phase.SafetyInstructionPresented += HandleSafetyInstruction;
            _phase.DangerPresented += HandleDanger;
        }

        private void OnDisable()
        {
            if (_phase != null)
            {
                _phase.ApproachInstructionPresented -= HandleApproachInstruction;
                _phase.SafetyInstructionPresented -= HandleSafetyInstruction;
                _phase.DangerPresented -= HandleDanger;
            }
            _activeVoice.Stop();
        }

        private void HandleApproachInstruction() => Play(_approachInstruction);
        private void HandleSafetyInstruction() => Play(_safetyInstruction);
        private void HandleDanger() => Play(_dangerWarning);

        private void Play(VoiceCueReference cue)
        {
            _activeVoice.Stop();
            AudioManager manager = AudioManager.Instance;
            if (manager != null) _activeVoice = manager.PlayVoice(cue);
        }
    }
}
