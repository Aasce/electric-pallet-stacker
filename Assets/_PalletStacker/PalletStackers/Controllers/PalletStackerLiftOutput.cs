using ElectricPalletStackers.Ble;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerLiftOutput : MonoBehaviour, IPalletStackerControlOutput
    {
        [SerializeField] private PalletStacker _palletStacker;

        private PalletStackerLiftState _lastLiftState = PalletStackerLiftState.Neutral;

        private void Awake()
        {
            if (_palletStacker == null) _palletStacker = GetComponent<PalletStacker>();
        }

        public void Apply(PalletStackerDriveCommand command)
        {
            if (_palletStacker == null || command.Lift == _lastLiftState) return;
            _lastLiftState = command.Lift;
            _palletStacker.SetLiftDirection(ToDirection(command.Lift));
        }

        public void StopImmediately()
        {
            _lastLiftState = PalletStackerLiftState.Neutral;
            _palletStacker?.SetLiftDirection(0f);
        }

        private static float ToDirection(PalletStackerLiftState lift)
        {
            return lift switch
            {
                PalletStackerLiftState.Up => 1f,
                PalletStackerLiftState.Down => -1f,
                _ => 0f
            };
        }
    }
}
