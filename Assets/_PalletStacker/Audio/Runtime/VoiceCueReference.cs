using System;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Serializable, data-owned reference to a voice cue in a <see cref="VoiceClipLibrary"/>.
    /// Gameplay code never needs to know an audio clip or file path.
    /// </summary>
    [Serializable]
    public struct VoiceCueReference
    {
        [SerializeField] private string _id;

        public string Id => _id;
        public bool IsEmpty => string.IsNullOrWhiteSpace(_id);

        public VoiceCueReference(string id)
        {
            _id = id;
        }

        public override string ToString()
        {
            return _id ?? string.Empty;
        }
    }
}
