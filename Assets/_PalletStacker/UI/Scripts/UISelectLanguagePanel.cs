using System;
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

        public UILanguageCode FocusedLanguage { get; private set; } = UILanguageCode.En;

        public event Action<UILanguageCode> FocusChanged;

        private void OnEnable()
        {
            SubscribeButtons();
        }

        private void OnDisable()
        {
            UnsubscribeButtons();
        }

        public override void ResetView()
        {
            FocusLanguage(_hasFocusedLanguage ? FocusedLanguage : UILanguageCode.En);
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

        private void FocusLanguage(UILanguageCode language)
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
            UILanguageCode language;
            if (button == _vietnameseButton)
                language = UILanguageCode.Vi;
            else if (button == _japaneseButton)
                language = UILanguageCode.Ja;
            else
                language = UILanguageCode.En;

            _englishButton?.SetFocused(language == UILanguageCode.En);
            _vietnameseButton?.SetFocused(language == UILanguageCode.Vi);
            _japaneseButton?.SetFocused(language == UILanguageCode.Ja);

            bool changed = !_hasFocusedLanguage || FocusedLanguage != language;
            _hasFocusedLanguage = true;
            FocusedLanguage = language;
            if (changed) FocusChanged?.Invoke(language);
        }

        private UICustomButton GetButton(UILanguageCode language)
        {
            switch (language)
            {
                case UILanguageCode.Vi:
                    return _vietnameseButton;
                case UILanguageCode.Ja:
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
