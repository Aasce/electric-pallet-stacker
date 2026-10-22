using System;
using System.Collections;
using System.Collections.Generic;
using ElectricPalletStackers.Audio;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PpeEquipmentPhase : MonoBehaviour
    {
        [Serializable]
        private sealed class EquipmentItem
        {
            [SerializeField] private GameObject _presentationRoot;
            [SerializeField] private XRSimpleInteractable _interactable;
            [SerializeField] private Collider _pokeCollider;

            public GameObject PresentationRoot => _presentationRoot;
            public XRSimpleInteractable Interactable => _interactable;
            public Collider PokeCollider => _pokeCollider;
        }

        [Header("Equipment")]
        [SerializeField] private EquipmentItem[] _equipmentItems = Array.Empty<EquipmentItem>();

        [Header("Completion")]
        [SerializeField, Min(0f)] private float _completionDelay = 1f;

        private readonly HashSet<XRSimpleInteractable> _equippedItems = new();
        private Coroutine _completionRoutine;

        public event Action Equipped;

        public bool IsPhaseActive { get; private set; }

        private void Awake()
        {
            SetPresentationActive(false);
        }

        private void OnEnable()
        {
            ForEachInteractable(interactable =>
                interactable.selectEntered.AddListener(HandleEquipmentSelected));
        }

        private void OnDisable()
        {
            ForEachInteractable(interactable =>
                interactable.selectEntered.RemoveListener(HandleEquipmentSelected));
            CancelPendingCompletion();
        }

        public void BeginPhase()
        {
            CancelPendingCompletion();
            _equippedItems.Clear();
            IsPhaseActive = true;
            SetPresentationActive(true);
        }

        public void EndPhase()
        {
            CancelPendingCompletion();
            IsPhaseActive = false;
            SetPresentationActive(false);
        }

        private void HandleEquipmentSelected(SelectEnterEventArgs args)
        {
            if (!IsPhaseActive || args.interactorObject is not XRPokeInteractor)
                return;

            if (args.interactableObject is not XRSimpleInteractable interactable ||
                !_equippedItems.Add(interactable))
                return;

            AudioManager.Instance?.Play(PalletStackerAudioKeys.EquipProtection);
            SetItemActive(FindItem(interactable), false);
            if (_equippedItems.Count < CountValidItems()) return;

            IsPhaseActive = false;
            _completionRoutine = StartCoroutine(CompleteAfterDelay());
        }

        private IEnumerator CompleteAfterDelay()
        {
            yield return new WaitForSeconds(_completionDelay);
            _completionRoutine = null;
            Equipped?.Invoke();
        }

        private void SetPresentationActive(bool active)
        {
            if (_equipmentItems == null) return;

            for (int index = 0; index < _equipmentItems.Length; index++)
                SetItemActive(_equipmentItems[index], active);
        }

        private static void SetItemActive(EquipmentItem item, bool active)
        {
            if (item == null) return;
            if (item.PresentationRoot != null) item.PresentationRoot.SetActive(active);
            if (item.PokeCollider != null) item.PokeCollider.enabled = active;
            if (item.Interactable != null) item.Interactable.enabled = active;
        }

        private EquipmentItem FindItem(XRSimpleInteractable interactable)
        {
            if (_equipmentItems == null) return null;

            for (int index = 0; index < _equipmentItems.Length; index++)
            {
                EquipmentItem item = _equipmentItems[index];
                if (item != null && item.Interactable == interactable) return item;
            }
            return null;
        }

        private int CountValidItems()
        {
            if (_equipmentItems == null) return 0;

            int count = 0;
            for (int index = 0; index < _equipmentItems.Length; index++)
            {
                if (_equipmentItems[index]?.Interactable != null) count++;
            }
            return count;
        }

        private void ForEachInteractable(Action<XRSimpleInteractable> action)
        {
            if (_equipmentItems == null) return;

            for (int index = 0; index < _equipmentItems.Length; index++)
            {
                XRSimpleInteractable interactable = _equipmentItems[index]?.Interactable;
                if (interactable != null) action(interactable);
            }
        }

        private void CancelPendingCompletion()
        {
            if (_completionRoutine == null) return;

            StopCoroutine(_completionRoutine);
            _completionRoutine = null;
        }
    }
}
