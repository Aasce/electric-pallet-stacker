using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    /// <summary>
    /// Keeps the XR playspace at the operator position behind the vehicle.
    /// Head tracking remains local to the playspace, while the playspace follows
    /// the pallet stacker's translation and heading.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class PalletStackerRearUserFollower : MonoBehaviour
    {
        [SerializeField] private Transform _userRig;
        [Tooltip("Vehicle-local operator position. The vehicle travels along local +Z, so -Z is behind it.")]
        [SerializeField] private Vector3 _localOperatorOffset = new Vector3(0f, 0f, -2.2f);
        [Tooltip("Rotates the rig's local +Z forward axis toward the vehicle's local +Z travel direction.")]
        [SerializeField] private Vector3 _localFacingEuler = Vector3.zero;
        [SerializeField] private bool _followPosition = true;
        [SerializeField] private bool _followRotation = true;

        public Transform UserRig => _userRig;
        public Vector3 LocalOperatorOffset => _localOperatorOffset;

        private void OnEnable()
        {
            FollowVehicle();
        }

        private void LateUpdate()
        {
            FollowVehicle();
        }

        [ContextMenu("Snap User Behind Vehicle")]
        public void FollowVehicle()
        {
            if (_userRig == null) return;

            if (_followPosition) _userRig.position = transform.TransformPoint(_localOperatorOffset);
            if (_followRotation)
            {
                Quaternion localFacing = Quaternion.Euler(_localFacingEuler);
                _userRig.rotation = transform.rotation * localFacing;
            }
        }

    }
}
