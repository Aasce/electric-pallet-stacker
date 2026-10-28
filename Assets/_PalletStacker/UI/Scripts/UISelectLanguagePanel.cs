using System;
using ElectricPalletStackers.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class UISelectLanguagePanel : UIPanel
    {
        [SerializeField] private UICustomButton _englishButton;
        [SerializeField] private UICustomButton _vietnameseButton;
        [SerializeField] private UICustomButton _japaneseButton;
        [SerializeField] private Button _confirmButton;

        private bool _hasFocusedLanguage;
        private bool _buttonsSubscribed;

        public LanguageCode FocusedLanguage { get; private set; } = LanguageCode.En;

        public event Action<LanguageCode> FocusChanged;
        public event Action<LanguageCode> Confirmed;

        private void OnEnable()
        {
            SubscribeButtons();
        }

        protected override void OnDisable()
        {
            UnsubscribeButtons();
            base.OnDisable();
        }

        public override void ResetView()
        {
            FocusLanguage(_hasFocusedLanguage ? FocusedLanguage : LanguageCode.En);
        }

        private void FocusLanguage(LanguageCode language)
        {
            UICustomButton button = GetButton(language);
            if (button != null) FocusButton(button);
        }

        private void FocusButton(UICustomButton button)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
                eventSystem.SetSelectedGameObject(button.gameObject);

            HandleButtonFocused(button);
        }

        private void HandleButtonFocused(UICustomButton button)
        {
            LanguageCode language;
            if (button == _vietnameseButton)
                language = LanguageCode.Vi;
            else if (button == _japaneseButton)
                language = LanguageCode.Ja;
            else
                language = LanguageCode.En;

            _englishButton?.SetFocused(language == LanguageCode.En);
            _vietnameseButton?.SetFocused(language == LanguageCode.Vi);
            _japaneseButton?.SetFocused(language == LanguageCode.Ja);

            bool changed = !_hasFocusedLanguage || FocusedLanguage != language;
            _hasFocusedLanguage = true;
            FocusedLanguage = language;
            if (changed) FocusChanged?.Invoke(language);
        }

        private UICustomButton GetButton(LanguageCode language)
        {
            switch (language)
            {
                case LanguageCode.Vi:
                    return _vietnameseButton;
                case LanguageCode.Ja:
                    return _japaneseButton;
                default:
                    return _englishButton;
            }
        }

        private void SubscribeButtons()
        {
            if (_buttonsSubscribed) return;
            SubscribeLanguageButton(_englishButton, SelectEnglish);
            SubscribeLanguageButton(_vietnameseButton, SelectVietnamese);
            SubscribeLanguageButton(_japaneseButton, SelectJapanese);
            if (_confirmButton != null)
                _confirmButton.onClick.AddListener(ConfirmLanguage);
            _buttonsSubscribed = true;
        }

        private void UnsubscribeButtons()
        {
            if (!_buttonsSubscribed) return;
            UnsubscribeLanguageButton(_englishButton, SelectEnglish);
            UnsubscribeLanguageButton(_vietnameseButton, SelectVietnamese);
            UnsubscribeLanguageButton(_japaneseButton, SelectJapanese);
            if (_confirmButton != null)
                _confirmButton.onClick.RemoveListener(ConfirmLanguage);
            _buttonsSubscribed = false;
        }

        private void SubscribeLanguageButton(
            UICustomButton button,
            UnityEngine.Events.UnityAction clickAction)
        {
            if (button == null) return;
            button.Focused += HandleButtonFocused;
            if (button.Button != null) button.Button.onClick.AddListener(clickAction);
        }

        private void UnsubscribeLanguageButton(
            UICustomButton button,
            UnityEngine.Events.UnityAction clickAction)
        {
            if (button == null) return;
            button.Focused -= HandleButtonFocused;
            if (button.Button != null) button.Button.onClick.RemoveListener(clickAction);
        }

        private void SelectEnglish() => FocusLanguage(LanguageCode.En);
        private void SelectVietnamese() => FocusLanguage(LanguageCode.Vi);
        private void SelectJapanese() => FocusLanguage(LanguageCode.Ja);
        private void ConfirmLanguage() => Confirmed?.Invoke(FocusedLanguage);

    }
}
