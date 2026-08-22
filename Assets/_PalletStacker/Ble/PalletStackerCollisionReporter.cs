using UnityEngine;
using UnityEngine.Events;

namespace ElectricPalletStackers.Ble
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerCollisionReporter : MonoBehaviour
    {
        [SerializeField] private PalletStackerCollisionSender _collisionSender;
        [SerializeField] private LayerMask _collisionLayers = ~0;
        [SerializeField, Min(0f)] private float _minimumRelativeSpeed;
        [SerializeField] private UnityEvent _onLocalCollisionDetected;

        private void Awake()
        {
            if (_collisionSender == null) _collisionSender = GetComponentInParent<PalletStackerCollisionSender>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            int layerMask = 1 << collision.gameObject.layer;
            if ((_collisionLayers.value & layerMask) == 0) return;
            if (collision.relativeVelocity.magnitude < _minimumRelativeSpeed) return;

            // Local gameplay should stop immediately; BLE confirmation is asynchronous.
            _onLocalCollisionDetected?.Invoke();
            _collisionSender?.SendCollision();
        }
    }
}
