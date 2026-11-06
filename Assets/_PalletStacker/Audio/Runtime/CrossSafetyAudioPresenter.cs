using ElectricPalletStackers.Gameplay;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    [DisallowMultipleComponent]
    public sealed class CrossSafetyAudioPresenter : MonoBehaviour
    {
        [SerializeField] private CrossSafetyPhaseController _crossSafety;

        private void OnEnable()
        {
            if (_crossSafety != null)
                _crossSafety.DirectionConfirmed += HandleDirectionConfirmed;
        }

        private void OnDisable()
        {
            if (_crossSafety != null)
                _crossSafety.DirectionConfirmed -= HandleDirectionConfirmed;
        }

        private static void HandleDirectionConfirmed()
        {
            AudioManager.Instance?.Play(PalletStackerAudioKeys.SafetyCheck);
        }
    }
}
