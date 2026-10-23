using System;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.Gameplay;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    /// <summary>
    /// Presents a one-time reminder at the beginning of each round when the
    /// vehicle has not yet been enabled by its ignition key.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleIgnitionReminder : MonoBehaviour
    {
        [SerializeField] private AppManager _appManager;
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField] private GameObject _presentationRoot;

        private bool _roundActive;
        private bool _dismissedForRound;

        public event Action<bool> VisibilityChanged;

        private void Awake()
        {
            ConfigurePresentation();
            Hide();
        }

        private void OnEnable()
        {
            if (_appManager != null)
            {
                _appManager.RoundStarted += HandleRoundStarted;
                _appManager.StateChanged += HandleGameStateChanged;
            }

            if (_stateReceiver != null)
                _stateReceiver.StateChanged += HandleControlStateChanged;
        }

        private void OnDisable()
        {
            if (_appManager != null)
            {
                _appManager.RoundStarted -= HandleRoundStarted;
                _appManager.StateChanged -= HandleGameStateChanged;
            }

            if (_stateReceiver != null)
                _stateReceiver.StateChanged -= HandleControlStateChanged;

            Hide();
        }

        private void HandleRoundStarted()
        {
            _roundActive = true;
            _dismissedForRound = IsVehicleEnabled();
            SetVisible(!_dismissedForRound);
        }

        private void HandleGameStateChanged(GameState previous, GameState next)
        {
            if (next == GameState.Playing) return;

            _roundActive = false;
            Hide();
        }

        private void HandleControlStateChanged(
            PalletStackerControlState state,
            PalletStackerControlFields changedFields)
        {
            if (!_roundActive || _dismissedForRound || state == null) return;
            if ((changedFields & PalletStackerControlFields.Enabled) == 0 ||
                !state.Enabled) return;

            _dismissedForRound = true;
            Hide();
        }

        private bool IsVehicleEnabled()
        {
            return _stateReceiver != null &&
                   _stateReceiver.CurrentState != null &&
                   _stateReceiver.CurrentState.Enabled;
        }

        private void ConfigurePresentation()
        {
            if (_presentationRoot == null) return;

            Canvas canvas = _presentationRoot.GetComponent<Canvas>();
            if (canvas == null) return;

            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue - 10;
        }

        private void SetVisible(bool visible)
        {
            if (_presentationRoot == null || _presentationRoot.activeSelf == visible) return;

            _presentationRoot.SetActive(visible);
            VisibilityChanged?.Invoke(visible);
        }

        private void Hide() => SetVisible(false);
    }
}
