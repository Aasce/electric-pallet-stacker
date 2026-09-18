using System;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class UIController : MonoBehaviour
    {
        [SerializeField] private UIInputRouter _inputRouter;
        [SerializeField] private UISelectLanguagePanel _selectLanguagePanel;
        [SerializeField] private UIWelcomePanel _welcomePanel;
        [SerializeField] private UIGuidePanel _guidePanel;
        [SerializeField] private UICompletedPanel _completedPanel;
        [SerializeField] private UIFailedPanel _failedPanel;

        public UIFlowState CurrentState { get; private set; } = UIFlowState.SelectLanguage;
        public bool IsInputCaptured => CurrentState != UIFlowState.Hidden;
        public UILanguageCode FocusedLanguage { get; private set; } = UILanguageCode.En;
        public UILanguageCode SelectedLanguage { get; private set; } = UILanguageCode.En;

        public event Action<UIFlowState> PanelChanged;
        public event Action<bool> InputCaptureChanged;
        public event Action<UILanguageCode> LanguagePreviewChanged;
        public event Action<UILanguageCode> LanguageConfirmed;
        /// <summary>
        /// Placeholder hook for systems that need to reset transient state before a new run.
        /// </summary>
        public event Action ResetRequested;

        private void Awake()
        {
            if (_selectLanguagePanel != null)
                _selectLanguagePanel.FocusChanged += HandleLanguageFocusChanged;

            _selectLanguagePanel?.Hide();
            _welcomePanel?.Hide();
            _guidePanel?.Hide();
            _completedPanel?.Hide();
            _failedPanel?.Hide();

            CurrentState = UIFlowState.SelectLanguage;
            _inputRouter?.ResetForPanel();
            _selectLanguagePanel?.Show();
            if (_selectLanguagePanel != null)
                FocusedLanguage = _selectLanguagePanel.FocusedLanguage;
        }

        private void OnEnable()
        {
            if (_inputRouter == null) return;
            _inputRouter.NavigateNext += HandleNavigateNext;
            _inputRouter.NavigatePrevious += HandleNavigatePrevious;
            _inputRouter.Confirm += HandleConfirm;
        }

        private void OnDisable()
        {
            if (_inputRouter == null) return;
            _inputRouter.NavigateNext -= HandleNavigateNext;
            _inputRouter.NavigatePrevious -= HandleNavigatePrevious;
            _inputRouter.Confirm -= HandleConfirm;
        }

        private void OnDestroy()
        {
            if (_selectLanguagePanel != null)
                _selectLanguagePanel.FocusChanged -= HandleLanguageFocusChanged;
        }

        private void HandleNavigateNext()
        {
            if (CurrentState == UIFlowState.SelectLanguage)
                _selectLanguagePanel?.NavigateNext();
        }

        private void HandleNavigatePrevious()
        {
            if (CurrentState == UIFlowState.SelectLanguage)
                _selectLanguagePanel?.NavigatePrevious();
        }

        private void HandleConfirm()
        {
            switch (CurrentState)
            {
                case UIFlowState.SelectLanguage:
                    SelectedLanguage = FocusedLanguage;
                    LanguageConfirmed?.Invoke(SelectedLanguage);
                    TransitionTo(UIFlowState.Welcome);
                    break;
                case UIFlowState.Welcome:
                    TransitionTo(UIFlowState.Guide);
                    break;
                case UIFlowState.Guide:
                    TransitionTo(UIFlowState.Hidden);
                    break;
                case UIFlowState.Completed:
                case UIFlowState.Failed:
                    ResetRequested?.Invoke();
                    TransitionTo(UIFlowState.Welcome);
                    break;
            }
        }

        public void ShowCompleted()
        {
            TransitionTo(UIFlowState.Completed);
        }

        public void ShowFailed()
        {
            TransitionTo(UIFlowState.Failed);
        }

        private void HandleLanguageFocusChanged(UILanguageCode language)
        {
            FocusedLanguage = language;
            LanguagePreviewChanged?.Invoke(language);
        }

        private void TransitionTo(UIFlowState nextState)
        {
            bool capturedBefore = IsInputCaptured;

            _selectLanguagePanel?.Hide();
            _welcomePanel?.Hide();
            _guidePanel?.Hide();
            _completedPanel?.Hide();
            _failedPanel?.Hide();

            CurrentState = nextState;
            _inputRouter?.ResetForPanel();

            switch (nextState)
            {
                case UIFlowState.SelectLanguage:
                    _selectLanguagePanel?.Show();
                    break;
                case UIFlowState.Welcome:
                    _welcomePanel?.Show();
                    break;
                case UIFlowState.Guide:
                    _guidePanel?.Show();
                    break;
                case UIFlowState.Completed:
                    _completedPanel?.Show();
                    break;
                case UIFlowState.Failed:
                    _failedPanel?.Show();
                    break;
            }

            PanelChanged?.Invoke(nextState);
            if (capturedBefore != IsInputCaptured)
                InputCaptureChanged?.Invoke(IsInputCaptured);
        }
    }
}
