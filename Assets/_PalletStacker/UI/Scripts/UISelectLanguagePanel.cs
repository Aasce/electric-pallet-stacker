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

        private bool _hasFocusedLanguage;
        private bool _buttonsSubscribed;

        public LanguageCode FocusedLanguage { get; private set; } = LanguageCode.En;

        public event Action<LanguageCode> FocusChanged;

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

        public void NavigateNext()
        {
            MoveFocus(useDownNavigation: true);
        }

        public void NavigatePrevious()
        {
            MoveFocus(useDownNavigation: false);
        }

        private void MoveFocus(bool useDownNavigation)
        {
            UICustomButton current = GetButton(FocusedLanguage);
            if (current == null) return;

            Navigation navigation = current.Button.navigation;
            Selectable target = useDownNavigation ? navigation.selectOnDown : navigation.selectOnUp;
            if (target == null) return;

            UICustomButton customButton = target.GetComponent<UICustomButton>();
            if (customButton != null) FocusButton(customButton);
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
            if (_englishButton != null) _englishButton.Focused += HandleButtonFocused;
            if (_vietnameseButton != null) _vietnameseButton.Focused += HandleButtonFocused;
            if (_japaneseButton != null) _japaneseButton.Focused += HandleButtonFocused;
            _buttonsSubscribed = true;
        }

        private void UnsubscribeButtons()
        {
            if (!_buttonsSubscribed) return;
            if (_englishButton != null) _englishButton.Focused -= HandleButtonFocused;
            if (_vietnameseButton != null) _vietnameseButton.Focused -= HandleButtonFocused;
            if (_japaneseButton != null) _japaneseButton.Focused -= HandleButtonFocused;
            _buttonsSubscribed = false;
        }

    }
}
