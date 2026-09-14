using ElectricPalletStackers.Ble;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    /// <summary>
    /// Explicitly connects the scene BLE services to the assigned gameplay targets.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class PalletStackerGameplayInstaller : MonoBehaviour
    {
        [Header("Scene services")]
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField] private PalletStackerCollisionSender _collisionSender;

        [Header("Gameplay target")]
        [SerializeField] private PalletStackerControlDriver _controlDriver;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;

        private void Awake()
        {
            _controlDriver?.Bind(_stateReceiver);
            _collisionReporter?.Bind(_collisionSender);
        }
    }
}
