using System;
using System.Collections.Generic;
using ElectricPalletStackers.Gameplay;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Converts game-loss events into data-configured localized voice cues.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameFailureVoicePresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class Binding
        {
            [SerializeField] private GameFailureReason _reason;
            [SerializeField] private VoiceCueReference _cue;

            public GameFailureReason Reason => _reason;
            public VoiceCueReference Cue => _cue;
        }

        [SerializeField] private AppManager _appManager;
        [SerializeField] private List<Binding> _bindings = new();

        private VoicePlaybackHandle _activeVoice;

        private void OnEnable()
        {
            if (_appManager == null) return;
            _appManager.GameLost += HandleGameLost;
            _appManager.RoundStarted += HandleRoundStarted;
        }

        private void OnDisable()
        {
            if (_appManager != null)
            {
                _appManager.GameLost -= HandleGameLost;
                _appManager.RoundStarted -= HandleRoundStarted;
            }
            _activeVoice.Stop();
        }

        private void HandleGameLost(GameFailureReason reason)
        {
            _activeVoice.Stop();
            for (int index = 0; index < _bindings.Count; index++)
            {
                Binding binding = _bindings[index];
                if (binding == null || binding.Reason != reason) continue;

                AudioManager manager = AudioManager.Instance;
                if (manager != null) _activeVoice = manager.PlayVoice(binding.Cue);
                return;
            }
        }

        private void HandleRoundStarted()
        {
            _activeVoice.Stop();
        }
    }
}
