using TMPro;

namespace ElectricPalletStackers.Localization
{
    public interface ILocalizationCatalog
    {
        bool TryGetText(LanguageCode language, string key, out string value);
        TMP_FontAsset GetFontOverride(LanguageCode language);
    }
}
