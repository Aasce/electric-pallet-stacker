using TMPro;
using UnityEngine;

namespace ElectricPalletStackers.Localization
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizedText : MonoBehaviour, ILocalizedView
    {
        [SerializeField] private TMP_Text _target;
        [SerializeField] private string _key;

        private TMP_FontAsset _defaultFont;

        public string Key => _key;

        public void ApplyLocalization(ILocalizationService localization)
        {
            if (localization == null) return;
            CacheDefaultFont();
            if (_target == null || string.IsNullOrWhiteSpace(_key)) return;

            _target.text = localization.GetText(_key);
            TMP_FontAsset overrideFont = localization.GetFontOverride();
            _target.font = overrideFont != null ? overrideFont : _defaultFont;
        }

        public void SetKey(string key, ILocalizationService localization)
        {
            _key = key;
            ApplyLocalization(localization);
        }

        public void SetText(string value, ILocalizationService localization)
        {
            CacheDefaultFont();
            if (_target == null) return;

            _key = string.Empty;
            _target.text = value ?? string.Empty;
            TMP_FontAsset overrideFont = localization?.GetFontOverride();
            _target.font = overrideFont != null ? overrideFont : _defaultFont;
        }

        private void CacheDefaultFont()
        {
            if (_defaultFont == null && _target != null) _defaultFont = _target.font;
        }
    }
}
