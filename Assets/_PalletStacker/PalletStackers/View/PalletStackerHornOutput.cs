using System;
using UnityEngine;
using UnityEngine.Events;

namespace ElectricPalletStackers.PalletStackers
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerHornOutput : MonoBehaviour, IPalletStackerControlOutput
    {
        [Serializable]
        public sealed class HornStateEvent : UnityEvent<bool>
        {
        }

        [SerializeField] private GameObject _activeIndicator;
        [SerializeField] private HornStateEvent _onHornChanged;

        public event Action<bool> HornChanged;

        public bool IsActive { get; private set; }

        public void Apply(PalletStackerDriveCommand command)
        {
            SetHorn(command.Horn);
        }

        public void StopImmediately()
        {
            SetHorn(false);
        }

        public void SetHorn(bool active)
        {
            if (IsActive == active) return;
            IsActive = active;

            if (_activeIndicator != null) _activeIndicator.SetActive(active);
            HornChanged?.Invoke(active);
            _onHornChanged?.Invoke(active);
        }

        private void OnDisable()
        {
            StopImmediately();
        }
    }
}
