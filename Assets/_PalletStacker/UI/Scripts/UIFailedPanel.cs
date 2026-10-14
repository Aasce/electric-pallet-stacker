using System;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.Localization;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricPalletStackers.UI
{
    public enum FailedPanelAction
    {
        ContinueFromCheckpoint,
        ExitToWelcome
    }

    [DisallowMultipleComponent]
    public sealed class UIFailedPanel : UIPanel
    {
        [Header("Content")]
        [SerializeField] private LocalizedText _reasonText;
        [SerializeField] private UICustomButton _continueButton;
        [SerializeField] private UICustomButton _exitButton;

        public FailedPanelAction SelectedAction { get; private set; } =
            FailedPanelAction.ContinueFromCheckpoint;

        public event Action<FailedPanelAction> ActionConfirmed;

        protected override void Awake()
        {
            base.Awake();
            _continueButton.Button.onClick.AddListener(ConfirmContinue);
            _exitButton.Button.onClick.AddListener(ConfirmExit);
        }

        private void OnDestroy()
        {
            _continueButton.Button.onClick.RemoveListener(ConfirmContinue);
            _exitButton.Button.onClick.RemoveListener(ConfirmExit);
        }

        public void SetFailureReason(
            GameFailureReason reason,
            GameFailureMessageCatalog catalog,
            LanguageCode language,
            ILocalizationService localization)
        {
            if (catalog != null && catalog.TryGet(reason, language, out GameFailureMessage message))
            {
                _reasonText.SetText($"{message.Cause}\n\n{message.Guidance}", localization);
                return;
            }

            Debug.LogError($"No failure message is configured for '{reason}'.", this);
            _reasonText.SetKey(LocalizationKeys.Failed.UnknownReason, localization);
        }

        public void NavigateNext()
        {
            Select(SelectedAction == FailedPanelAction.ContinueFromCheckpoint
                ? FailedPanelAction.ExitToWelcome
                : FailedPanelAction.ContinueFromCheckpoint);
        }

        public void NavigatePrevious()
        {
            NavigateNext();
        }

        public void ConfirmSelection()
        {
            ActionConfirmed?.Invoke(SelectedAction);
        }

        public override void ResetView()
        {
            Select(FailedPanelAction.ContinueFromCheckpoint);
        }

        private void ConfirmContinue()
        {
            Select(FailedPanelAction.ContinueFromCheckpoint);
            ConfirmSelection();
        }

        private void ConfirmExit()
        {
            Select(FailedPanelAction.ExitToWelcome);
            ConfirmSelection();
        }

        private void Select(FailedPanelAction action)
        {
            SelectedAction = action;
            _continueButton.SetFocused(action == FailedPanelAction.ContinueFromCheckpoint);
            _exitButton.SetFocused(action == FailedPanelAction.ExitToWelcome);

            UICustomButton selected = action == FailedPanelAction.ContinueFromCheckpoint
                ? _continueButton
                : _exitButton;
            EventSystem.current.SetSelectedGameObject(selected.gameObject);
        }
    }
}
