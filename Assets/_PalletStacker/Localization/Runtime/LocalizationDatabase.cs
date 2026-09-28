using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ElectricPalletStackers.Localization
{
    [CreateAssetMenu(
        fileName = "Localization Database",
        menuName = "Electric Pallet Stacker/Localization Database")]
    public sealed class LocalizationDatabase : ScriptableObject, ILocalizationCatalog
    {
        [SerializeField] private List<LocalizedStringEntry> _entries = new();
        [SerializeField] private List<LocalizedLanguageProfile> _languageProfiles = new();

        private Dictionary<string, LocalizedStringEntry> _entriesByKey;
        private Dictionary<LanguageCode, TMP_FontAsset> _fontsByLanguage;

        public bool TryGetText(LanguageCode language, string key, out string value)
        {
            EnsureCache();
            if (!string.IsNullOrWhiteSpace(key) &&
                _entriesByKey.TryGetValue(key, out LocalizedStringEntry entry))
            {
                value = entry.GetText(language);
                return !string.IsNullOrEmpty(value);
            }

            value = string.Empty;
            return false;
        }

        public TMP_FontAsset GetFontOverride(LanguageCode language)
        {
            EnsureCache();
            return _fontsByLanguage.TryGetValue(language, out TMP_FontAsset font) ? font : null;
        }

        private void OnEnable()
        {
            InvalidateCache();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            InvalidateCache();
        }
#endif

        private void EnsureCache()
        {
            if (_entriesByKey != null && _fontsByLanguage != null) return;

            _entriesByKey = new Dictionary<string, LocalizedStringEntry>(StringComparer.Ordinal);
            for (int index = 0; index < _entries.Count; index++)
            {
                LocalizedStringEntry entry = _entries[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key)) continue;

                if (!_entriesByKey.TryAdd(entry.Key, entry))
                    Debug.LogWarning($"Duplicate localization key ignored: '{entry.Key}'.", this);
            }

            _fontsByLanguage = new Dictionary<LanguageCode, TMP_FontAsset>();
            for (int index = 0; index < _languageProfiles.Count; index++)
            {
                LocalizedLanguageProfile profile = _languageProfiles[index];
                if (profile == null || profile.FontOverride == null) continue;
                _fontsByLanguage[profile.Language] = profile.FontOverride;
            }
        }

        private void InvalidateCache()
        {
            _entriesByKey = null;
            _fontsByLanguage = null;
        }
    }

    [Serializable]
    public sealed class LocalizedStringEntry
    {
        [SerializeField] private string _key;
        [SerializeField, TextArea] private string _english;
        [SerializeField, TextArea] private string _vietnamese;
        [SerializeField, TextArea] private string _japanese;

        public string Key => _key;

        public string GetText(LanguageCode language)
        {
            return language switch
            {
                LanguageCode.Vi => _vietnamese,
                LanguageCode.Ja => _japanese,
                _ => _english
            };
        }
    }

    [Serializable]
    public sealed class LocalizedLanguageProfile
    {
        [SerializeField] private LanguageCode _language;
        [SerializeField] private TMP_FontAsset _fontOverride;

        public LanguageCode Language => _language;
        public TMP_FontAsset FontOverride => _fontOverride;
    }
}
