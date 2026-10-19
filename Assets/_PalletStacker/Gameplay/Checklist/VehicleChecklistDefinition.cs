using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay.Checklist
{
    [CreateAssetMenu(
        fileName = "Vehicle Checklist",
        menuName = "Electric Pallet Stacker/Vehicle Checklist")]
    public sealed class VehicleChecklistDefinition : ScriptableObject
    {
        [SerializeField] private List<VehicleChecklistEntry> _entries = new();

        public IReadOnlyList<VehicleChecklistEntry> Entries => _entries;
    }

    [Serializable]
    public sealed class VehicleChecklistEntry
    {
        [SerializeField] private string _localizationKey;

        public string LocalizationKey => _localizationKey;
    }
}
