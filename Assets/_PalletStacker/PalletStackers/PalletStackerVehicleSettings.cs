using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    [CreateAssetMenu(
        fileName = "Pallet Stacker Vehicle Settings",
        menuName = "Electric Pallet Stacker/Vehicle Settings")]
    public sealed class PalletStackerVehicleSettings : ScriptableObject
    {
        public const float DefaultMaximumForwardSpeed = 2.5f;
        public const float DefaultMaximumReverseSpeed = 1.5f;
        public const float DefaultAcceleration = 3f;
        public const float DefaultServiceBrakeDeceleration = 6f;
        public const float DefaultMaximumSteeringAngleDegrees = 45f;
        public const float DefaultSlowModeTravelMultiplier = 0.4f;
        public const float DefaultMinimumForkHeight = 0.3f;
        public const float DefaultMaximumForkHeight = 2f;
        public const float DefaultForkLiftSpeed = 1f;

        [Header("Travel")]
        [Tooltip("Maximum forward travel speed in metres per second.")]
        [SerializeField, Min(0f)] private float _maximumForwardSpeed = DefaultMaximumForwardSpeed;
        [Tooltip("Maximum reverse travel speed in metres per second.")]
        [SerializeField, Min(0f)] private float _maximumReverseSpeed = DefaultMaximumReverseSpeed;
        [Tooltip("Travel acceleration in metres per second squared.")]
        [SerializeField, Min(0.01f)] private float _acceleration = DefaultAcceleration;
        [Tooltip("Service-brake deceleration in metres per second squared.")]
        [SerializeField, Min(0.01f)] private float _serviceBrakeDeceleration = DefaultServiceBrakeDeceleration;
        [Tooltip("Multiplier applied to the travel command while slow mode is active.")]
        [SerializeField, Range(0.05f, 1f)] private float _slowModeTravelMultiplier = DefaultSlowModeTravelMultiplier;

        [Header("Steering")]
        [SerializeField, Range(1f, 80f)] private float _maximumSteeringAngleDegrees = DefaultMaximumSteeringAngleDegrees;

        [Header("Fork lift")]
        [Tooltip("Minimum fork local height in metres.")]
        [SerializeField] private float _minimumForkHeight = DefaultMinimumForkHeight;
        [Tooltip("Maximum fork local height in metres.")]
        [SerializeField] private float _maximumForkHeight = DefaultMaximumForkHeight;
        [Tooltip("Fork vertical speed in metres per second.")]
        [SerializeField, Min(0f)] private float _forkLiftSpeed = DefaultForkLiftSpeed;

        public float MaximumForwardSpeed => Mathf.Max(0f, _maximumForwardSpeed);
        public float MaximumReverseSpeed => Mathf.Max(0f, _maximumReverseSpeed);
        public float Acceleration => Mathf.Max(0.01f, _acceleration);
        public float ServiceBrakeDeceleration => Mathf.Max(0.01f, _serviceBrakeDeceleration);
        public float SlowModeTravelMultiplier => Mathf.Clamp(_slowModeTravelMultiplier, 0.05f, 1f);
        public float MaximumSteeringAngleDegrees => Mathf.Clamp(_maximumSteeringAngleDegrees, 1f, 80f);
        public float MinimumForkHeight => _minimumForkHeight;
        public float MaximumForkHeight => Mathf.Max(_minimumForkHeight, _maximumForkHeight);
        public float ForkLiftSpeed => Mathf.Max(0f, _forkLiftSpeed);
    }
}
