using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class RoundPoseResetter : MonoBehaviour, IGameResettable
    {
        [Header("Vehicle start pose")]
        [SerializeField] private Rigidbody _vehicleBody;
        [Tooltip("Authoritative start anchor. Move this Transform in the scene to change the reset pose.")]
        [SerializeField] private Transform _vehicleStartPose;

        [Header("Vehicle state")]
        [SerializeField] private PalletStackerMast _mast;
        [SerializeField] private PalletStackerRearUserFollower _playerFollower;

        public void ResetState()
        {
            _mast?.ResetToMinimumHeight();

            if (_vehicleBody != null && _vehicleStartPose != null)
            {
                RigidbodyPoseUtility.Restore(
                    _vehicleBody,
                    _vehicleStartPose.position,
                    _vehicleStartPose.rotation,
                    true);
            }

            // The player/camera rig is owned by this follower, so snapping it after the
            // vehicle restores both vehicle and operator to their authored start poses.
            _playerFollower?.FollowVehicle();
            Physics.SyncTransforms();
        }

    }

    /// <summary>
    /// Performs an authoritative Rigidbody teleport without leaving interpolation or
    /// velocity state from the previous round to overwrite the restored pose.
    /// </summary>
    internal static class RigidbodyPoseUtility
    {
        public static void Restore(
            Rigidbody body,
            Vector3 position,
            Quaternion rotation,
            bool sleep)
        {
            if (body == null) return;

            RigidbodyInterpolation interpolation = body.interpolation;
            body.interpolation = RigidbodyInterpolation.None;

            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);

            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                if (sleep) body.Sleep();
                else body.WakeUp();
            }

            body.interpolation = interpolation;
        }
    }
}
