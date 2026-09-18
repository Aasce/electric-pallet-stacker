using System;
using System.Collections.Generic;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PalletDestinationZone : MonoBehaviour
    {
        [SerializeField] private BoxCollider _trigger;
        [SerializeField, Min(0f)] private float _confirmationDuration = 1f;
        [SerializeField] private GameObject _activeVisual;

        private readonly HashSet<Collider> _overlappingTargetColliders = new();
        private PalletStackerLoad _targetPallet;
        private PalletStackerLoadHandler _loadHandler;
        private float _validDuration;
        private bool _confirmed;

        public bool IsArmed => _targetPallet != null && _trigger != null && _trigger.enabled;
        public float ConfirmationProgress => _confirmationDuration <= 0f
            ? (IsValidNow() ? 1f : 0f)
            : Mathf.Clamp01(_validDuration / _confirmationDuration);

        public event Action<PalletDestinationZone, PalletStackerLoad> DestinationConfirmed;

        private void Awake()
        {
            if (_trigger != null && !_trigger.isTrigger)
                Debug.LogError("PalletDestinationZone requires its BoxCollider to be a trigger.", this);

            Disarm();
        }

        private void Update()
        {
            if (_confirmed || !IsArmed) return;

            if (!IsValidNow())
            {
                _validDuration = 0f;
                return;
            }

            _validDuration += Time.deltaTime;
            if (_validDuration < _confirmationDuration) return;

            _confirmed = true;
            DestinationConfirmed?.Invoke(this, _targetPallet);
        }

        private void OnTriggerEnter(Collider other)
        {
            TrackTargetCollider(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TrackTargetCollider(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            _overlappingTargetColliders.Remove(other);
            if (_overlappingTargetColliders.Count == 0) _validDuration = 0f;
        }

        public void Arm(PalletStackerLoad targetPallet, PalletStackerLoadHandler loadHandler)
        {
            _targetPallet = targetPallet;
            _loadHandler = loadHandler;
            _validDuration = 0f;
            _confirmed = false;
            _overlappingTargetColliders.Clear();

            if (_trigger != null) _trigger.enabled = targetPallet != null;
            if (_activeVisual != null) _activeVisual.SetActive(targetPallet != null);
        }

        public void Disarm()
        {
            if (_trigger != null) _trigger.enabled = false;
            if (_activeVisual != null) _activeVisual.SetActive(false);

            _targetPallet = null;
            _loadHandler = null;
            _validDuration = 0f;
            _confirmed = false;
            _overlappingTargetColliders.Clear();
        }

        private void TrackTargetCollider(Collider candidate)
        {
            if (!IsArmed || candidate == null) return;
            if (candidate.GetComponentInParent<PalletStackerLoad>() != _targetPallet) return;

            _overlappingTargetColliders.Add(candidate);
        }

        private bool IsValidNow()
        {
            if (_targetPallet == null || !_targetPallet.isActiveAndEnabled) return false;
            if (_overlappingTargetColliders.Count == 0) return false;

            // A pallet touching the destination while still held by the forks is not delivered.
            return _loadHandler == null || _loadHandler.HeldLoad != _targetPallet;
        }

    }
}
