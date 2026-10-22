using ElectricPalletStackers.Localization;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    /// <summary>
    /// Keeps AudioManager narration language aligned with the text localization service.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceLanguageSync : MonoBehaviour
    {
        [SerializeField] private LocalizationManager _localizationManager;

        private void OnEnable()
        {
            if (_localizationManager == null) return;
            _localizationManager.LanguageChanged += HandleLanguageChanged;
            HandleLanguageChanged(_localizationManager.CurrentLanguage);
        }

        private void OnDisable()
        {
            if (_localizationManager != null)
                _localizationManager.LanguageChanged -= HandleLanguageChanged;
        }

        private static void HandleLanguageChanged(LanguageCode language)
        {
            AudioManager.Instance?.SetVoiceLanguage(language);
        }
    }
}
