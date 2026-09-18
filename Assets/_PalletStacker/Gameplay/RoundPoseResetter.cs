using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class RoundPoseResetter : MonoBehaviour, IGameResettable
    {
        [SerializeField] private Rigidbody _vehicleBody;
        [SerializeField] private PalletStackerMast _mast;
        [SerializeField] private PalletStackerRearUserFollower _playerFollower;

        private Vector3 _initialVehiclePosition;
        private Quaternion _initialVehicleRotation;

        private void Awake()
        {
            if (_vehicleBody == null) return;
            _initialVehiclePosition = _vehicleBody.position;
            _initialVehicleRotation = _vehicleBody.rotation;
        }

        public void ResetState()
        {
            _mast?.ResetToMinimumHeight();

            if (_vehicleBody != null)
            {
                _vehicleBody.linearVelocity = Vector3.zero;
                _vehicleBody.angularVelocity = Vector3.zero;
                _vehicleBody.position = _initialVehiclePosition;
                _vehicleBody.rotation = _initialVehicleRotation;
                _vehicleBody.Sleep();
            }

            // The player/camera rig is owned by this follower, so snapping it after the
            // vehicle restores both vehicle and operator to their authored start poses.
            _playerFollower?.FollowVehicle();
            Physics.SyncTransforms();
        }

    }
}
