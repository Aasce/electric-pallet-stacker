using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    [CreateAssetMenu(fileName = "Audio Cue Library", menuName = "Electric Pallet Stacker/Audio/Cue Library")]
    public sealed class AudioCueLibrary : ScriptableObject
    {
        [Serializable]
        private sealed class Entry
        {
            [SerializeField] private string _key;
            [SerializeField] private AudioCueConfig _config = new();

            public string Key => _key;
            public AudioCueConfig Config => _config;
        }

        [SerializeField] private List<Entry> _entries = new();

        private readonly Dictionary<string, AudioCueConfig> _lookup =
            new(StringComparer.OrdinalIgnoreCase);

        public bool TryGet(string key, out AudioCueConfig config)
        {
            if (_lookup.Count == 0) RebuildLookup();
            config = null;
            return !string.IsNullOrWhiteSpace(key) && _lookup.TryGetValue(key, out config);
        }

        private void OnEnable()
        {
            RebuildLookup();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            RebuildLookup();
        }
#endif

        private void RebuildLookup()
        {
            _lookup.Clear();
            for (int index = 0; index < _entries.Count; index++)
            {
                Entry entry = _entries[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key) || entry.Config == null)
                    continue;

                string key = entry.Key.Trim();
                if (_lookup.ContainsKey(key))
                {
                    Debug.LogWarning($"Duplicate audio key '{key}' in '{name}'.", this);
                    continue;
                }

                _lookup.Add(key, entry.Config);
            }
        }
    }
}
