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
        [Tooltip("Explicit pose used by ResetPallet. Usually the authored pallet spawn anchor.")]
        [SerializeField] private Transform _resetPoint;
        [SerializeField] private Transform[] _spawnPoints;
        [SerializeField] private bool _randomizeSpawn;

        public PalletStackerLoad CurrentPallet => _pallet;
        public IReadOnlyList<Transform> SpawnPoints => _spawnPoints;

        public PalletStackerLoad SpawnRandom()
        {
            if (_pallet == null)
            {
                Debug.LogError("A scene pallet must be assigned before starting a round.", this);
                return null;
            }

            Transform spawnPoint = _randomizeSpawn
                ? GetRandomValidSpawnPoint()
                : GetFirstValidSpawnPoint();
            if (spawnPoint == null)
            {
                Debug.LogError("At least one valid pallet spawn point must be assigned.", this);
                return null;
            }

            if (!_pallet.gameObject.activeSelf) _pallet.gameObject.SetActive(true);
            _pallet.SetHighlighted(true);

            Rigidbody body = _pallet.Body;
            if (body != null)
            {
                RigidbodyPoseUtility.Restore(
                    body,
                    spawnPoint.position,
                    spawnPoint.rotation,
                    false);
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
            if (_pallet == null || _resetPoint == null) return;

            _pallet.SetHighlighted(true);
            Rigidbody body = _pallet.Body;
            if (body != null)
            {
                RigidbodyPoseUtility.Restore(
                    body,
                    _resetPoint.position,
                    _resetPoint.rotation,
                    true);
            }
            else
            {
                _pallet.transform.SetPositionAndRotation(
                    _resetPoint.position,
                    _resetPoint.rotation);
            }

            _pallet.ResetSupportTracking();
            Physics.SyncTransforms();
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

        private Transform GetFirstValidSpawnPoint()
        {
            if (_spawnPoints == null) return null;
            for (int index = 0; index < _spawnPoints.Length; index++)
                if (_spawnPoints[index] != null) return _spawnPoints[index];
            return null;
        }
    }
}
