using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class VehicleLocalPoseFollower : MonoBehaviour
    {
        [SerializeField] private Transform _vehicle;
        [SerializeField] private Vector3 _localPosition = new(-0.45f, 1.25f, -0.7f);
        [SerializeField] private Vector3 _localEulerAngles = Vector3.zero;

        private void OnEnable()
        {
            ApplyPose();
        }

        private void LateUpdate()
        {
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (_vehicle == null) return;

            transform.SetPositionAndRotation(
                _vehicle.TransformPoint(_localPosition),
                _vehicle.rotation * Quaternion.Euler(_localEulerAngles));
        }
    }
}
