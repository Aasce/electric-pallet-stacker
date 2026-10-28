using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricPalletStackers.UI
{
    public enum ResetGameAction
    {
        Restart,
        Continue,
        ResetPhase
    }

    /// <summary>
    /// Reset-menu view. It owns selection visuals and emits the selected command only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIResetGamePanel : UIPanel
    {
        [SerializeField] private UICustomButton _restartButton;
        [SerializeField] private UICustomButton _continueButton;
        [SerializeField] private UICustomButton _resetPhaseButton;

        public ResetGameAction SelectedAction { get; private set; } = ResetGameAction.Continue;

        public event Action<ResetGameAction> ActionConfirmed;

        protected override void Awake()
        {
            base.Awake();
            AddButtonListeners();
        }

        private void OnDestroy()
        {
            RemoveButtonListeners();
        }

        public void ConfirmSelection()
        {
            ActionConfirmed?.Invoke(SelectedAction);
        }

        public override void ResetView()
        {
            Select(ResetGameAction.Continue);
        }

        private void AddButtonListeners()
        {
            if (_restartButton != null)
                _restartButton.Button.onClick.AddListener(ConfirmRestart);
            if (_continueButton != null)
                _continueButton.Button.onClick.AddListener(ConfirmContinue);
            if (_resetPhaseButton != null)
                _resetPhaseButton.Button.onClick.AddListener(ConfirmResetPhase);
        }

        private void RemoveButtonListeners()
        {
            if (_restartButton != null)
                _restartButton.Button.onClick.RemoveListener(ConfirmRestart);
            if (_continueButton != null)
                _continueButton.Button.onClick.RemoveListener(ConfirmContinue);
            if (_resetPhaseButton != null)
                _resetPhaseButton.Button.onClick.RemoveListener(ConfirmResetPhase);
        }

        private void ConfirmRestart()
        {
            Select(ResetGameAction.Restart);
            ConfirmSelection();
        }

        private void ConfirmContinue()
        {
            Select(ResetGameAction.Continue);
            ConfirmSelection();
        }

        private void ConfirmResetPhase()
        {
            Select(ResetGameAction.ResetPhase);
            ConfirmSelection();
        }

        private void Select(ResetGameAction action)
        {
            SelectedAction = action;
            _restartButton?.SetFocused(action == ResetGameAction.Restart);
            _continueButton?.SetFocused(action == ResetGameAction.Continue);
            _resetPhaseButton?.SetFocused(action == ResetGameAction.ResetPhase);

            UICustomButton selected = action switch
            {
                ResetGameAction.Restart => _restartButton,
                ResetGameAction.ResetPhase => _resetPhaseButton,
                _ => _continueButton
            };
            if (selected != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(selected.gameObject);
        }
    }
}
