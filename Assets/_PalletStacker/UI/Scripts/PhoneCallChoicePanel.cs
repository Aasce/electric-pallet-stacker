using System;
using ElectricPalletStackers.Localization;
using TMPro;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class PhoneCallChoicePanel : UIPanel, ILocalizedView
    {
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private UICustomButton _stopAndListenButton;
        [SerializeField] private UICustomButton _moveWhileListeningButton;

        private bool _buttonsBound;
        private TMP_FontAsset _titleDefaultFont;
        private TMP_FontAsset _stopDefaultFont;
        private TMP_FontAsset _moveDefaultFont;

        public event Action StopAndListenSelected;
        public event Action MoveWhileListeningSelected;

        protected override void Awake()
        {
            base.Awake();
            if (PanelCanvasGroup != null)
                PanelCanvasGroup.ignoreParentGroups = true;
            EnablePokeTarget(_stopAndListenButton);
            EnablePokeTarget(_moveWhileListeningButton);
            BindButtons();
        }

        private void OnEnable()
        {
            EnablePokeTarget(_stopAndListenButton);
            EnablePokeTarget(_moveWhileListeningButton);
            BindButtons();
        }

        protected override void OnDisable()
        {
            UnbindButtons();
            base.OnDisable();
        }

        public void ApplyLocalization(ILocalizationService localization)
        {
            CacheDefaultFonts();
            SetText(
                _titleLabel,
                LocalizationKeys.Phone.ChoiceTitle,
                "Choose how to take this call",
                _titleDefaultFont,
                localization);
            SetText(
                _stopAndListenButton?.Label,
                LocalizationKeys.Phone.StopAndListen,
                "Stop and answer",
                _stopDefaultFont,
                localization);
            SetText(
                _moveWhileListeningButton?.Label,
                LocalizationKeys.Phone.MoveWhileListening,
                "Move while answering",
                _moveDefaultFont,
                localization);
        }

        private void BindButtons()
        {
            if (_buttonsBound) return;
            if (_stopAndListenButton != null)
                _stopAndListenButton.Button.onClick.AddListener(ChooseStopAndListen);
            if (_moveWhileListeningButton != null)
                _moveWhileListeningButton.Button.onClick.AddListener(ChooseMoveWhileListening);
            _buttonsBound = true;
        }

        private void UnbindButtons()
        {
            if (!_buttonsBound) return;
            if (_stopAndListenButton != null)
                _stopAndListenButton.Button.onClick.RemoveListener(ChooseStopAndListen);
            if (_moveWhileListeningButton != null)
                _moveWhileListeningButton.Button.onClick.RemoveListener(ChooseMoveWhileListening);
            _buttonsBound = false;
        }

        private void ChooseStopAndListen()
        {
            StopAndListenSelected?.Invoke();
        }

        private void ChooseMoveWhileListening()
        {
            MoveWhileListeningSelected?.Invoke();
        }

        private static void EnablePokeTarget(UICustomButton button)
        {
            if (button?.Button?.targetGraphic != null)
                button.Button.targetGraphic.raycastTarget = true;
        }

        private void CacheDefaultFonts()
        {
            if (_titleDefaultFont == null && _titleLabel != null)
                _titleDefaultFont = _titleLabel.font;
            if (_stopDefaultFont == null && _stopAndListenButton?.Label != null)
                _stopDefaultFont = _stopAndListenButton.Label.font;
            if (_moveDefaultFont == null && _moveWhileListeningButton?.Label != null)
                _moveDefaultFont = _moveWhileListeningButton.Label.font;
        }

        private static void SetText(
            TMP_Text target,
            string key,
            string fallback,
            TMP_FontAsset defaultFont,
            ILocalizationService localization)
        {
            if (target == null) return;
            target.text = localization != null ? localization.GetText(key) : fallback;
            TMP_FontAsset fontOverride = localization?.GetFontOverride();
            target.font = fontOverride != null ? fontOverride : defaultFont;
        }
    }
}
