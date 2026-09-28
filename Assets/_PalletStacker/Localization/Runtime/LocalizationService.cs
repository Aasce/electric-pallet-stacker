using System;
using TMPro;

namespace ElectricPalletStackers.Localization
{
    public sealed class LocalizationService : ILocalizationService
    {
        private readonly ILocalizationCatalog _catalog;
        private readonly LanguageCode _fallbackLanguage;

        public LocalizationService(
            ILocalizationCatalog catalog,
            LanguageCode initialLanguage = LanguageCode.En,
            LanguageCode fallbackLanguage = LanguageCode.En)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            CurrentLanguage = initialLanguage;
            _fallbackLanguage = fallbackLanguage;
        }

        public LanguageCode CurrentLanguage { get; private set; }

        public event Action<LanguageCode> LanguageChanged;

        public void SetLanguage(LanguageCode language)
        {
            if (CurrentLanguage == language) return;
            CurrentLanguage = language;
            LanguageChanged?.Invoke(language);
        }

        public string GetText(string key)
        {
            return TryGetText(key, out string value) ? value : $"[{key}]";
        }

        public bool TryGetText(string key, out string value)
        {
            if (_catalog.TryGetText(CurrentLanguage, key, out value)) return true;
            if (CurrentLanguage != _fallbackLanguage &&
                _catalog.TryGetText(_fallbackLanguage, key, out value)) return true;

            value = string.Empty;
            return false;
        }

        public TMP_FontAsset GetFontOverride()
        {
            return _catalog.GetFontOverride(CurrentLanguage);
        }
    }
}
