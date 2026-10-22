using System;
using System.Collections.Generic;
using ElectricPalletStackers.Localization;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    [CreateAssetMenu(
        fileName = "Voice Clip Library",
        menuName = "Electric Pallet Stacker/Audio/Voice Clip Library")]
    public sealed class VoiceClipLibrary : ScriptableObject
    {
        [Serializable]
        private sealed class Entry
        {
            [SerializeField] private string _id;
            [SerializeField] private string _category;
            [SerializeField] private string _priority;

            [Header("Localized clips")]
            [SerializeField] private AudioClip _vietnamese;
            [SerializeField] private AudioClip _english;
            [SerializeField] private AudioClip _japanese;

            public string Id => _id;

            public AudioClip Resolve(LanguageCode language)
            {
                AudioClip localizedClip = language switch
                {
                    LanguageCode.Vi => _vietnamese,
                    LanguageCode.Ja => _japanese,
                    _ => _english
                };

                // English is the single fallback. If it is also null, stay silent.
                return localizedClip != null ? localizedClip : _english;
            }
        }

        [SerializeField] private List<Entry> _entries = new();

        private readonly Dictionary<string, Entry> _lookup =
            new(StringComparer.OrdinalIgnoreCase);

        public bool TryResolve(
            VoiceCueReference cue,
            LanguageCode language,
            out AudioClip clip)
        {
            clip = null;
            if (cue.IsEmpty) return false;
            if (_lookup.Count == 0) RebuildLookup();
            if (!_lookup.TryGetValue(cue.Id, out Entry entry)) return false;

            clip = entry.Resolve(language);
            return clip != null;
        }

        private void OnEnable()
        {
            RebuildLookup();
        }

        private void OnValidate()
        {
            RebuildLookup();
        }

        private void RebuildLookup()
        {
            _lookup.Clear();
            for (int index = 0; index < _entries.Count; index++)
            {
                Entry entry = _entries[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id)) continue;

                string id = entry.Id.Trim();
                if (_lookup.ContainsKey(id))
                {
                    Debug.LogWarning($"Duplicate voice cue id '{id}' in '{name}'.", this);
                    continue;
                }

                _lookup.Add(id, entry);
            }
        }
    }
}
