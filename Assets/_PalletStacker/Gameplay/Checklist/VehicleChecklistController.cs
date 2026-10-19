using System;
using System.Collections;
using System.Collections.Generic;
using ElectricPalletStackers.Localization;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay.Checklist
{
    [DisallowMultipleComponent]
    public sealed class VehicleChecklistController : MonoBehaviour, ILocalizedView
    {
        [SerializeField] private GameObject _presentationRoot;
        [SerializeField] private VehicleChecklistDefinition _definition;
        [SerializeField] private VehicleChecklistItemView _itemPrefab;
        [SerializeField] private RectTransform _itemsRoot;
        [SerializeField] private LocalizationManager _localizationManager;
        [SerializeField, Min(0f)] private float _completionDelay = 2f;

        private readonly List<VehicleChecklistItemView> _items = new();
        private ILocalizationService _localization;
        private int _checkedCount;
        private Coroutine _completionRoutine;

        public event Action Completed;

        public bool IsPhaseActive { get; private set; }

        public void BeginPhase()
        {
            CancelPendingCompletion();
            EnsureItemsCreated();
            ResetItems();
            IsPhaseActive = true;
            _presentationRoot.SetActive(true);
        }

        public void EndPhase()
        {
            CancelPendingCompletion();
            IsPhaseActive = false;
            _presentationRoot.SetActive(false);
        }

        public void ApplyLocalization(ILocalizationService localization)
        {
            _localization = localization;

            for (int index = 0; index < _items.Count; index++)
                _items[index].ApplyLocalization(localization);
        }

        private void EnsureItemsCreated()
        {
            if (_items.Count > 0) return;

            _localization = _localizationManager.Service;
            IReadOnlyList<VehicleChecklistEntry> entries = _definition.Entries;
            for (int index = 0; index < entries.Count; index++)
            {
                VehicleChecklistEntry entry = entries[index];
                VehicleChecklistItemView item = Instantiate(_itemPrefab, _itemsRoot);
                item.name = $"Checklist Item {index + 1:00}";
                item.Configure(entry.LocalizationKey, _localization);
                item.Checked += HandleItemChecked;
                _items.Add(item);
            }
        }

        private void ResetItems()
        {
            _checkedCount = 0;
            for (int index = 0; index < _items.Count; index++)
                _items[index].ResetState();
        }

        private void HandleItemChecked()
        {
            if (!IsPhaseActive) return;

            _checkedCount++;
            if (_checkedCount != _items.Count) return;

            IsPhaseActive = false;
            _completionRoutine = StartCoroutine(CompleteAfterDelay());
        }

        private IEnumerator CompleteAfterDelay()
        {
            yield return new WaitForSeconds(_completionDelay);
            _completionRoutine = null;
            Completed?.Invoke();
        }

        private void CancelPendingCompletion()
        {
            if (_completionRoutine == null) return;

            StopCoroutine(_completionRoutine);
            _completionRoutine = null;
        }
    }
}
