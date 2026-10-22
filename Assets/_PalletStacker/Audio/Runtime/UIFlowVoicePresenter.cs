using System;
using System.Collections.Generic;
using ElectricPalletStackers.UI;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Presentation adapter from UI flow events to localized voice cues.
    /// State-to-cue mappings live in serialized data rather than code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIFlowVoicePresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class Binding
        {
            [SerializeField] private UIFlowState _state;
            [SerializeField] private VoiceCueReference _cue;

            public UIFlowState State => _state;
            public VoiceCueReference Cue => _cue;
        }

        [SerializeField] private UIController _controller;
        [SerializeField] private List<Binding> _bindings = new();
        [SerializeField] private bool _playCurrentStateOnStart = true;

        private VoicePlaybackHandle _activeVoice;

        private void OnEnable()
        {
            if (_controller != null) _controller.PanelChanged += HandlePanelChanged;
        }

        private void Start()
        {
            if (_playCurrentStateOnStart && _controller != null)
                HandlePanelChanged(_controller.CurrentState);
        }

        private void OnDisable()
        {
            if (_controller != null) _controller.PanelChanged -= HandlePanelChanged;
            _activeVoice.Stop();
        }

        private void HandlePanelChanged(UIFlowState state)
        {
            _activeVoice.Stop();
            for (int index = 0; index < _bindings.Count; index++)
            {
                Binding binding = _bindings[index];
                if (binding == null || binding.State != state) continue;

                AudioManager manager = AudioManager.Instance;
                if (manager != null) _activeVoice = manager.PlayVoice(binding.Cue);
                return;
            }
        }
    }
}
