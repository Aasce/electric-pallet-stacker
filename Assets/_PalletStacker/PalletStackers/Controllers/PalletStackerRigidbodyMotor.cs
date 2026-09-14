using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PalletStackerRigidbodyMotor : MonoBehaviour, IPalletStackerControlOutput
    {
        [Header("Dependencies")]
        [SerializeField] private Rigidbody _body;

        [Header("Vehicle geometry")]
        [Tooltip("Local-space forward axis. The current simple model faces local +X.")]
        [SerializeField] private Vector3 _localForwardAxis = Vector3.right;
        [Tooltip("Center of the fixed front axle. The current model's wide Wheel mesh represents this axle.")]
        [SerializeField] private Transform _fixedFrontAxleReference;
        [Tooltip("Center of the virtual steered and driven rear axle. Place it longitudinally under the Steering Column.")]
        [SerializeField] private Transform _steeredRearAxleReference;
        [Tooltip("Fallback wheelbase used when either axle reference is missing.")]
        [SerializeField, Min(0.01f)] private float _wheelBaseMeters = 1.2f;
        [SerializeField, Range(1f, 80f)] private float _maximumSteeringAngleDegrees = 45f;

        [Header("Speed")]
        [SerializeField, Min(0f)] private float _maximumForwardSpeed = 2.5f;
        [SerializeField, Min(0f)] private float _maximumReverseSpeed = 1.5f;
        [SerializeField, Min(0.01f)] private float _acceleration = 3f;
        [SerializeField, Min(0.01f)] private float _serviceBrakeDeceleration = 6f;

        private float _targetSpeed;
        private float _currentSpeed;
        private float _steeringDegrees;
        private bool _movementInhibited = true;

        public float CurrentSpeed => _currentSpeed;
        public float TargetSpeed => _targetSpeed;

        private void Awake()
        {
            EnforceYawOnlyRotation();
            SnapRotationToYaw();
        }

        private void OnDisable()
        {
            StopImmediately();
        }

        private void EnforceYawOnlyRotation()
        {
            if (_body == null) return;

            RigidbodyConstraints constraints = _body.constraints;
            constraints |= RigidbodyConstraints.FreezeRotationX;
            constraints |= RigidbodyConstraints.FreezeRotationZ;
            constraints &= ~RigidbodyConstraints.FreezeRotationY;
            _body.constraints = constraints;
        }

        public void Apply(PalletStackerDriveCommand command)
        {
            _movementInhibited = command.MovementInhibited;
            _steeringDegrees = Mathf.Clamp(
                command.SteeringDegrees,
                -_maximumSteeringAngleDegrees,
                _maximumSteeringAngleDegrees);

            float speedLimit = command.TravelNormalized >= 0f
                ? _maximumForwardSpeed
                : _maximumReverseSpeed;
            _targetSpeed = _movementInhibited
                ? 0f
                : Mathf.Clamp(command.TravelNormalized, -1f, 1f) * speedLimit;

            if (command.RequiresImmediateStop) StopImmediately();
        }

        public void StopImmediately()
        {
            _targetSpeed = 0f;
            _currentSpeed = 0f;
            _movementInhibited = true;

            if (_body == null) return;
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            SnapRotationToYaw();
        }

        private void FixedUpdate()
        {
            if (_body == null) return;

            float rate = ShouldBrake() ? _serviceBrakeDeceleration : _acceleration;
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, _targetSpeed, rate * Time.fixedDeltaTime);
            if (Mathf.Abs(_currentSpeed) < 0.0001f)
            {
                StopBodyMotion();
                return;
            }

            float deltaTime = Time.fixedDeltaTime;
            Quaternion rotation = GetYawOnlyRotation();
            Vector3 localForward = SafeLocalForward();
            Vector3 worldForward = rotation * localForward;
            Vector3 frontAxleLocalPosition = GetFrontAxleLocalPosition(localForward);
            Vector3 frontAxleWorldOffset = rotation * frontAxleLocalPosition;
            float wheelBase = GetEffectiveWheelBase(localForward);

            // SteeringDegrees expresses the requested turn direction of the fixed front end.
            // The virtual rear wheels point the opposite way and provide the drive force.
            float steerRadians = _steeringDegrees * Mathf.Deg2Rad;
            float frontAxleSpeed = _currentSpeed * Mathf.Cos(steerRadians);
            float yawRateRadians = _currentSpeed / wheelBase * Mathf.Sin(steerRadians);
            float yawDegrees = yawRateRadians * Mathf.Rad2Deg * deltaTime;
            Quaternion nextRotation = Quaternion.AngleAxis(yawDegrees, Vector3.up) * rotation;
            Vector3 frontAxlePosition = _body.position + frontAxleWorldOffset;
            Vector3 frontAxleDisplacement = GetFrontAxleArcDisplacement(
                worldForward,
                frontAxleSpeed,
                yawRateRadians,
                deltaTime);
            Vector3 nextPosition =
                frontAxlePosition + frontAxleDisplacement - nextRotation * frontAxleLocalPosition;

            if (_body.isKinematic)
            {
                _body.MovePosition(nextPosition);
                _body.MoveRotation(nextRotation);
            }
            else
            {
                _body.linearVelocity = (nextPosition - _body.position) / deltaTime;
                _body.angularVelocity = Vector3.zero;
                _body.MoveRotation(nextRotation);
            }
        }

        private bool ShouldBrake()
        {
            if (_movementInhibited) return true;
            if (Mathf.Approximately(_targetSpeed, 0f)) return true;
            return Mathf.Sign(_currentSpeed) != Mathf.Sign(_targetSpeed) && !Mathf.Approximately(_currentSpeed, 0f);
        }

        private Vector3 SafeLocalForward()
        {
            Vector3 planar = Vector3.ProjectOnPlane(_localForwardAxis, Vector3.up);
            return planar.sqrMagnitude > 0.0001f ? planar.normalized : Vector3.forward;
        }

        private static Vector3 GetFrontAxleArcDisplacement(
            Vector3 worldForward,
            float frontAxleSpeed,
            float yawRateRadians,
            float deltaTime)
        {
            float halfYaw = yawRateRadians * deltaTime * 0.5f;
            float sinc = Mathf.Abs(halfYaw) < 0.0001f
                ? 1f
                : Mathf.Sin(halfYaw) / halfYaw;
            Quaternion midpointYaw =
                Quaternion.AngleAxis(halfYaw * Mathf.Rad2Deg, Vector3.up);

            return midpointYaw * worldForward * (frontAxleSpeed * deltaTime * sinc);
        }

        private Vector3 GetFrontAxleLocalPosition(Vector3 localForward)
        {
            if (_fixedFrontAxleReference != null)
            {
                return transform.InverseTransformPoint(_fixedFrontAxleReference.position);
            }

            return localForward * GetEffectiveWheelBase(localForward);
        }

        private float GetEffectiveWheelBase(Vector3 localForward)
        {
            if (_fixedFrontAxleReference != null && _steeredRearAxleReference != null)
            {
                Vector3 axleSeparation = transform.InverseTransformPoint(_fixedFrontAxleReference.position)
                    - transform.InverseTransformPoint(_steeredRearAxleReference.position);
                float referencedWheelBase = Mathf.Abs(Vector3.Dot(axleSeparation, localForward));
                if (referencedWheelBase >= 0.01f) return referencedWheelBase;
            }

            return Mathf.Max(0.01f, _wheelBaseMeters);
        }

        private void StopBodyMotion()
        {
            if (_body == null) return;
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            SnapRotationToYaw();
        }

        private Quaternion GetYawOnlyRotation()
        {
            float yaw = _body != null ? _body.rotation.eulerAngles.y : transform.eulerAngles.y;
            return Quaternion.Euler(0f, yaw, 0f);
        }

        private void SnapRotationToYaw()
        {
            if (_body == null) return;
            _body.rotation = GetYawOnlyRotation();
        }
    }
}
