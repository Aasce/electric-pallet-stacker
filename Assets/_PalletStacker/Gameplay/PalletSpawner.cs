using System.Collections.Generic;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PalletSpawner : MonoBehaviour
    {
        [Tooltip("The single pallet instance placed in the scene.")]
        [SerializeField] private PalletStackerLoad _pallet;
        [SerializeField] private Transform[] _spawnPoints;

        private Vector3 _initialPosition;
        private Quaternion _initialRotation;

        public PalletStackerLoad CurrentPallet => _pallet;
        public IReadOnlyList<Transform> SpawnPoints => _spawnPoints;

        private void Awake()
        {
            if (_pallet == null) return;
            _initialPosition = _pallet.transform.position;
            _initialRotation = _pallet.transform.rotation;
        }

        public PalletStackerLoad SpawnRandom()
        {
            if (_pallet == null)
            {
                Debug.LogError("A scene pallet must be assigned before starting a round.", this);
                return null;
            }

            Transform spawnPoint = GetRandomValidSpawnPoint();
            if (spawnPoint == null)
            {
                Debug.LogError("At least one valid pallet spawn point must be assigned.", this);
                return null;
            }

            if (!_pallet.gameObject.activeSelf) _pallet.gameObject.SetActive(true);

            Rigidbody body = _pallet.Body;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = spawnPoint.position;
                body.rotation = spawnPoint.rotation;
                body.WakeUp();
            }
            else
            {
                _pallet.transform.SetPositionAndRotation(
                    spawnPoint.position,
                    spawnPoint.rotation);
            }

            _pallet.ResetSupportTracking();
            return _pallet;
        }

        public void ResetPallet()
        {
            if (_pallet == null) return;

            Rigidbody body = _pallet.Body;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = _initialPosition;
                body.rotation = _initialRotation;
                body.Sleep();
            }
            else
            {
                _pallet.transform.SetPositionAndRotation(_initialPosition, _initialRotation);
            }

            _pallet.ResetSupportTracking();
        }

        private Transform GetRandomValidSpawnPoint()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0) return null;

            int validCount = 0;
            for (int index = 0; index < _spawnPoints.Length; index++)
            {
                if (_spawnPoints[index] != null) validCount++;
            }

            if (validCount == 0) return null;

            int selectedValidIndex = Random.Range(0, validCount);
            for (int index = 0; index < _spawnPoints.Length; index++)
            {
                Transform candidate = _spawnPoints[index];
                if (candidate == null) continue;
                if (selectedValidIndex-- == 0) return candidate;
            }

            return null;
        }
    }
}
