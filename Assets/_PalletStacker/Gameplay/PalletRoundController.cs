using System;
using System.Collections.Generic;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PalletRoundController : MonoBehaviour, IGameRoundParticipant, IGameVictorySource, IGameResettable
    {
        [SerializeField] private PalletSpawner _palletSpawner;
        [SerializeField] private PalletStackerLoadHandler _loadHandler;
        [Tooltip("Separate scene instances that share the same destination prefab.")]
        [SerializeField] private PalletDestinationZone[] _destinationZones;

        private PalletDestinationZone _activeDestination;

        public PalletStackerLoad ActivePallet => _palletSpawner != null
            ? _palletSpawner.CurrentPallet
            : null;
        public PalletDestinationZone ActiveDestination => _activeDestination;
        public IReadOnlyList<PalletDestinationZone> DestinationZones => _destinationZones;

        public event Action VictoryRequested;

        public void PrepareRound()
        {
            DeactivateDestinations();
            _loadHandler?.ReleaseLoad();

            PalletStackerLoad pallet = _palletSpawner != null
                ? _palletSpawner.SpawnRandom()
                : null;
            if (pallet == null)
            {
                Debug.LogError("The round cannot start without a spawned pallet.", this);
                return;
            }

            _activeDestination = GetRandomValidDestination();
            if (_activeDestination == null)
            {
                Debug.LogError("At least one valid destination must be assigned.", this);
                return;
            }

            _activeDestination.DestinationConfirmed += HandleDestinationConfirmed;
            _activeDestination.Arm(pallet, _loadHandler);
        }

        public void FinishRound(GameState result)
        {
            DeactivateDestinations();
        }

        public void ResetState()
        {
            DeactivateDestinations();
            _loadHandler?.ReleaseLoad();
            _palletSpawner?.ResetPallet();
        }

        private void OnDisable()
        {
            DeactivateDestinations();
        }

        private void HandleDestinationConfirmed(
            PalletDestinationZone destination,
            PalletStackerLoad pallet)
        {
            if (destination != _activeDestination || pallet != ActivePallet) return;
            VictoryRequested?.Invoke();
        }

        private void DeactivateDestinations()
        {
            if (_activeDestination != null)
                _activeDestination.DestinationConfirmed -= HandleDestinationConfirmed;

            if (_destinationZones != null)
            {
                for (int index = 0; index < _destinationZones.Length; index++)
                    _destinationZones[index]?.Disarm();
            }

            _activeDestination = null;
        }

        private PalletDestinationZone GetRandomValidDestination()
        {
            if (_destinationZones == null || _destinationZones.Length == 0) return null;

            int validCount = 0;
            for (int index = 0; index < _destinationZones.Length; index++)
            {
                if (_destinationZones[index] != null) validCount++;
            }

            if (validCount == 0) return null;

            int selectedValidIndex = UnityEngine.Random.Range(0, validCount);
            for (int index = 0; index < _destinationZones.Length; index++)
            {
                PalletDestinationZone candidate = _destinationZones[index];
                if (candidate == null) continue;
                if (selectedValidIndex-- == 0) return candidate;
            }

            return null;
        }
    }
}
