using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PpeEquipmentPhase : MonoBehaviour
    {
        [Header("Prefab References")]
        [SerializeField] private GameObject _helmetVisual;
        [SerializeField] private GameObject _instructionText;
        [SerializeField] private XRSimpleInteractable _helmetInteractable;
        [SerializeField] private Collider _pokeCollider;

        public event Action Equipped;

        public bool IsPhaseActive { get; private set; }

        private void Awake()
        {
            SetPresentationActive(false);
        }

        private void OnEnable()
        {
            if (_helmetInteractable != null)
                _helmetInteractable.selectEntered.AddListener(HandleHelmetSelected);
        }

        private void OnDisable()
        {
            if (_helmetInteractable != null)
                _helmetInteractable.selectEntered.RemoveListener(HandleHelmetSelected);
        }

        public void BeginPhase()
        {
            IsPhaseActive = true;
            SetPresentationActive(true);
        }

        public void EndPhase()
        {
            IsPhaseActive = false;
            SetPresentationActive(false);
        }

        private void HandleHelmetSelected(SelectEnterEventArgs args)
        {
            if (!IsPhaseActive || args.interactorObject is not XRPokeInteractor)
                return;

            IsPhaseActive = false;
            SetPresentationActive(false);
            Equipped?.Invoke();
        }

        private void SetPresentationActive(bool active)
        {
            if (_helmetVisual != null) _helmetVisual.SetActive(active);
            if (_instructionText != null) _instructionText.SetActive(active);
            if (_pokeCollider != null) _pokeCollider.enabled = active;
            if (_helmetInteractable != null) _helmetInteractable.enabled = active;
        }
    }
}
