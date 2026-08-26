using ElectricPalletStackers.Ble;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    /// <summary>
    /// Scene composition root. It is the only place that knows both the BLE
    /// transport objects and the pallet stacker gameplay objects.
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
            ResolveGameplayTarget();
            _controlDriver?.Bind(_stateReceiver);
            _collisionReporter?.Bind(_collisionSender);
        }

        private void ResolveGameplayTarget()
        {
            if (_controlDriver == null)
                _controlDriver = FindFirstObjectByType<PalletStackerControlDriver>();
            if (_collisionReporter == null && _controlDriver != null)
                _collisionReporter = _controlDriver.GetComponent<PalletStackerCollisionReporter>();
            if (_collisionReporter == null)
                _collisionReporter = FindFirstObjectByType<PalletStackerCollisionReporter>();
        }
    }
}
