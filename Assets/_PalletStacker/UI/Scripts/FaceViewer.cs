using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class FaceViewer : MonoBehaviour
    {
        [SerializeField] private Transform _viewer;

        private void OnEnable()
        {
            UpdateFacing();
        }

        private void LateUpdate()
        {
            UpdateFacing();
        }

        private void UpdateFacing()
        {
            if (_viewer == null) return;

            Vector3 positiveZDirection = transform.position - _viewer.position;
            if (positiveZDirection.sqrMagnitude < 0.000001f) return;

            // World-space UI is authored with its visible face along local -Z.
            transform.rotation = Quaternion.LookRotation(
                positiveZDirection.normalized,
                Vector3.up);
        }
    }
}
