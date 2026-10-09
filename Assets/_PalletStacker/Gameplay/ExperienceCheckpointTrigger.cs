using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    public enum ExperienceCheckpoint
    {
        Corner,
        YieldIntersection,
        PhoneIntersection
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ExperienceCheckpointTrigger : MonoBehaviour
    {
        [SerializeField] private TrainingExperienceController _experience;
        [SerializeField] private ExperienceCheckpoint _checkpoint;
        private bool _triggered;

        public void ResetTrigger() => _triggered = false;

        private void OnTriggerEnter(Collider other)
        {
            if (_triggered || other == null ||
                other.GetComponentInParent<PalletStacker>() == null) return;
            if (_experience != null && _experience.ReachCheckpoint(_checkpoint)) _triggered = true;
        }
    }
}
