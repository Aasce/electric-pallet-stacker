using System;
using ElectricPalletStackers.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.Gameplay.Checklist
{
    [DisallowMultipleComponent]
    public sealed class VehicleChecklistItemView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Toggle _toggle;

        private string _localizationKey;
        private TMP_FontAsset _defaultFont;

        public event Action Checked;

        private void Awake()
        {
            CacheDefaultFont();
        }

        private void OnEnable()
        {
            _toggle.onValueChanged.AddListener(HandleValueChanged);
        }

        private void OnDisable()
        {
            _toggle.onValueChanged.RemoveListener(HandleValueChanged);
        }

        public void Configure(string localizationKey, ILocalizationService localization)
        {
            _localizationKey = localizationKey;
            ApplyLocalization(localization);
            ResetState();
        }

        public void ApplyLocalization(ILocalizationService localization)
        {
            CacheDefaultFont();
            _label.text = localization.GetText(_localizationKey);
            TMP_FontAsset fontOverride = localization.GetFontOverride();
            _label.font = fontOverride != null ? fontOverride : _defaultFont;
        }

        public void ResetState()
        {
            _toggle.SetIsOnWithoutNotify(false);
            _toggle.interactable = true;
        }

        private void HandleValueChanged(bool isChecked)
        {
            if (!isChecked) return;

            _toggle.interactable = false;
            Checked?.Invoke();
        }

        private void CacheDefaultFont()
        {
            if (_defaultFont == null) _defaultFont = _label.font;
        }
    }
}
