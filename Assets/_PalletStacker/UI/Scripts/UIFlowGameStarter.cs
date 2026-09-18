using ElectricPalletStackers.Gameplay;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    /// <summary>
    /// Presentation-side adapter that starts gameplay after the existing UI flow closes.
    /// Gameplay remains independent from UI types.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIFlowGameStarter : MonoBehaviour
    {
        [SerializeField] private UIController _uiController;
        [SerializeField] private AppManager _appManager;

        private bool _hasStarted;

        private void OnEnable()
        {
            if (_uiController != null)
            {
                _uiController.PanelChanged += HandlePanelChanged;
                _uiController.ResetRequested += HandleResetRequested;
            }

            if (_appManager != null)
                _appManager.GameWon += HandleGameWon;

            TryStartFromCurrentState();
        }

        private void OnDisable()
        {
            if (_uiController != null)
            {
                _uiController.PanelChanged -= HandlePanelChanged;
                _uiController.ResetRequested -= HandleResetRequested;
            }

            if (_appManager != null)
                _appManager.GameWon -= HandleGameWon;
        }

        private void HandlePanelChanged(UIFlowState state)
        {
            if (state == UIFlowState.Hidden) TryStartGame();
        }

        private void TryStartFromCurrentState()
        {
            if (_uiController != null && _uiController.CurrentState == UIFlowState.Hidden)
                TryStartGame();
        }

        private void TryStartGame()
        {
            if (_hasStarted || _appManager == null) return;

            _hasStarted = true;
            _appManager.StartRound();
        }

        private void HandleGameWon()
        {
            _uiController?.ShowCompleted();
        }

        private void HandleResetRequested()
        {
            _hasStarted = false;

            // TODO: Reset any additional gameplay and presentation state here.
            // The next transition to Hidden will start a freshly prepared round.
        }
    }
}
