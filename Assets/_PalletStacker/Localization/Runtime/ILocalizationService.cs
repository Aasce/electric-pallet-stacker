using System;
using TMPro;

namespace ElectricPalletStackers.Localization
{
    public interface ILocalizationService
    {
        LanguageCode CurrentLanguage { get; }
        event Action<LanguageCode> LanguageChanged;

        void SetLanguage(LanguageCode language);
        string GetText(string key);
        bool TryGetText(string key, out string value);
        TMP_FontAsset GetFontOverride();
    }
}
