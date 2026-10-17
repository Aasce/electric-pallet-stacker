using System.Collections.Generic;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    public enum RearUserRotationMode
    {
        ComfortFollow,
        WorldLocked,
        VehicleLocked
    }

    /// <summary>
    /// Keeps the XR playspace at the operator position behind the vehicle.
    /// Translation follows the pallet stacker while rotation can be world locked,
    /// vehicle locked, or comfort filtered.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class PalletStackerRearUserFollower : MonoBehaviour
    {
        private static readonly int ApertureSizeId = Shader.PropertyToID("_ApertureSize");
        private static readonly int FeatheringEffectId = Shader.PropertyToID("_FeatheringEffect");

        [Header("Rig")]
        [SerializeField] private Transform _userRig;
        [SerializeField] private CharacterController[] _playerControllers;
        [SerializeField] private Collider[] _vehicleColliders;
        [Tooltip("Optional vehicle child that defines the exact player follow pose. When assigned, it replaces the numeric position and facing offsets below.")]
        [SerializeField] private Transform _followAnchor;
        [Tooltip("Vehicle-local operator position. The vehicle travels along local +Z, so -Z is behind it.")]
        [SerializeField] private Vector3 _localOperatorOffset = new Vector3(0f, 0f, -2.2f);
        [Tooltip("Rotates the rig's local +Z forward axis toward the vehicle's local +Z travel direction.")]
        [SerializeField] private Vector3 _localFacingEuler = Vector3.zero;
        [SerializeField] private bool _followPosition = true;
        [SerializeField] private bool _followRotation = true;

        [Header("Rotation comfort")]
        [SerializeField] private RearUserRotationMode _rotationMode = RearUserRotationMode.ComfortFollow;
        [Tooltip("Tracked HMD assigned explicitly from the scene.")]
        [SerializeField] private Transform _head;
        [SerializeField, Range(0f, 45f)] private float _rotationDeadZoneDegrees = 10f;
        [SerializeField, Min(0f)] private float _maximumComfortYawSpeed = 25f;
        [SerializeField, Min(0.01f)] private float _comfortYawSmoothTime = 0.18f;
        [SerializeField, Range(0f, 180f)] private float _snapAlignmentThresholdDegrees = 50f;
        [SerializeField, Range(0f, 90f)] private float _snapAlignmentStepDegrees = 30f;
        [SerializeField, Min(0f)] private float _snapCooldownSeconds = 0.35f;

        [Header("Turn vignette")]
        [Tooltip("Optional comfort vignette. Can also be changed at runtime with SetTurnVignetteEnabled.")]
        [SerializeField] private bool _useTurnVignette = true;
        [SerializeField] private TurnVignetteView _turnVignetteViewPrefab;
        [Tooltip("The vignette closes when the filtered yaw speed rises above this value.")]
        [SerializeField, Min(0f)] private float _vignetteEngageYawSpeed = 8f;
        [Tooltip("The vignette opens only after the filtered yaw speed falls below this lower value.")]
        [SerializeField, Min(0f)] private float _vignetteReleaseYawSpeed = 3f;
        [SerializeField, Min(0.01f)] private float _vignetteFullYawSpeed = 45f;
        [SerializeField, Min(0.01f)] private float _vignetteYawSmoothingTime = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _minimumVignetteAperture = 0.68f;
        [SerializeField, Range(0f, 1f)] private float _vignetteFeathering = 0.2f;
        [SerializeField, Min(0.01f)] private float _vignetteEaseInTime = 0.12f;
        [SerializeField, Min(0.01f)] private float _vignetteEaseOutTime = 0.2f;

        private bool _hasFollowState;
        private Vector3 _lastOperatorPosition;
        private float _lastVehicleYaw;
        private float _comfortYawVelocity;
        private float _snapCooldownRemaining;
        private TurnVignetteView _vignetteInstance;
        private Renderer _vignetteRenderer;
        private MaterialPropertyBlock _vignetteProperties;
        private float _currentVignetteAperture = 1f;
        private float _smoothedVignetteYawSpeed;
        private bool _isVignetteEngaged;
        private readonly List<IgnoredCollisionPair> _ignoredPlayerVehicleCollisions = new();

        public Transform UserRig => _userRig;
        public Transform FollowAnchor => _followAnchor;
        public Vector3 LocalOperatorOffset => _localOperatorOffset;
        public bool TurnVignetteEnabled => _useTurnVignette;

        private void OnEnable()
        {
            IgnorePlayerVehicleCollisions();
            EnsureVignette();
            FollowVehicle();
        }

        private void OnDisable()
        {
            RestorePlayerVehicleCollisions();
            _hasFollowState = false;
            _comfortYawVelocity = 0f;
            ResetVignetteState(true);
        }

        private void LateUpdate()
        {
            UpdateFollower();
        }

        [ContextMenu("Snap User Behind Vehicle")]
        public void FollowVehicle()
        {
            if (_userRig == null) return;

            if (_ignoredPlayerVehicleCollisions.Count == 0)
                IgnorePlayerVehicleCollisions();

            Vector3 operatorPosition = GetOperatorPosition();
            if (_followPosition) _userRig.position = operatorPosition;

            float vehicleYaw = GetVehicleFacingYaw();
            if (_followRotation)
            {
                switch (_rotationMode)
                {
                    case RearUserRotationMode.ComfortFollow:
                        RotateRigAroundHead(vehicleYaw);
                        break;
                    case RearUserRotationMode.VehicleLocked:
                        // The XR origin is the vehicle-space anchor. Rotating around the
                        // tracked head would leave its non-zero room-scale offset behind
                        // when the vehicle turns, which appears as drift in a headset.
                        RotateRigAroundPivot(vehicleYaw, _userRig.position);
                        break;
                }
            }

            _lastOperatorPosition = operatorPosition;
            _lastVehicleYaw = vehicleYaw;
            _comfortYawVelocity = 0f;
            _snapCooldownRemaining = 0f;
            _hasFollowState = true;
            ResetVignetteState(false);
        }

        /// <summary>
        /// Enables or disables the optional turn vignette. This method is suitable
        /// for a UI Toggle's dynamic bool event.
        /// </summary>
        public void SetTurnVignetteEnabled(bool enabled)
        {
            if (_useTurnVignette == enabled) return;

            _useTurnVignette = enabled;
            ResetVignetteState(!enabled);
            if (enabled) EnsureVignette();
        }

        private void UpdateFollower()
        {
            if (_userRig == null) return;
            if (!_hasFollowState)
            {
                FollowVehicle();
                return;
            }

            EnsureVignette();

            Vector3 operatorPosition = GetOperatorPosition();
            float vehicleYaw = GetVehicleFacingYaw();
            Vector3 operatorDelta = operatorPosition - _lastOperatorPosition;
            float vehicleYawDelta = Mathf.DeltaAngle(_lastVehicleYaw, vehicleYaw);
            float rigYawBeforeRotation = _userRig.eulerAngles.y;
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);

            if (_rotationMode == RearUserRotationMode.VehicleLocked)
            {
                ApplyVehicleLockedMotion(operatorDelta, vehicleYawDelta);
            }
            else
            {
                if (_followPosition)
                    _userRig.position += operatorDelta;

                if (_followRotation && _rotationMode == RearUserRotationMode.ComfortFollow)
                    ApplyComfortRotation(vehicleYaw, deltaTime);
            }

            _lastOperatorPosition = operatorPosition;

            float vehicleYawSpeed = Mathf.Abs(Mathf.DeltaAngle(_lastVehicleYaw, vehicleYaw)) / deltaTime;
            _lastVehicleYaw = vehicleYaw;

            float appliedYawSpeed = Mathf.Abs(
                Mathf.DeltaAngle(rigYawBeforeRotation, _userRig.eulerAngles.y)) / deltaTime;
            UpdateTurnVignette(Mathf.Max(vehicleYawSpeed, appliedYawSpeed), deltaTime);
        }

        private void ApplyVehicleLockedMotion(Vector3 operatorDelta, float vehicleYawDelta)
        {
            if (_followRotation && Mathf.Abs(vehicleYawDelta) >= 0.001f)
            {
                // Apply the same rigid transform as the vehicle/operator frame. This
                // rotates the HMD's tracked positional offset with the vehicle instead
                // of pinning the HMD in world space while the XR origin turns beneath it.
                Vector3 pivot = _followPosition ? _lastOperatorPosition : _userRig.position;
                Quaternion yawRotation = Quaternion.AngleAxis(vehicleYawDelta, Vector3.up);
                _userRig.position = pivot + yawRotation * (_userRig.position - pivot);
                _userRig.rotation = yawRotation * _userRig.rotation;
            }

            if (_followPosition)
                _userRig.position += operatorDelta;
        }

        private void ApplyComfortRotation(float targetYaw, float deltaTime)
        {
            _snapCooldownRemaining = Mathf.Max(0f, _snapCooldownRemaining - deltaTime);
            float currentYaw = _userRig.eulerAngles.y;
            float error = Mathf.DeltaAngle(currentYaw, targetYaw);
            float absoluteError = Mathf.Abs(error);
            if (absoluteError <= _rotationDeadZoneDegrees)
            {
                _comfortYawVelocity = 0f;
                return;
            }

            if (_snapCooldownRemaining <= 0f &&
                _snapAlignmentStepDegrees > 0f &&
                absoluteError >= Mathf.Max(_snapAlignmentThresholdDegrees, _rotationDeadZoneDegrees))
            {
                float snapSize = Mathf.Min(
                    _snapAlignmentStepDegrees,
                    absoluteError - _rotationDeadZoneDegrees);
                RotateRigAroundHead(currentYaw + Mathf.Sign(error) * snapSize);
                _comfortYawVelocity = 0f;
                _snapCooldownRemaining = _snapCooldownSeconds;
                return;
            }

            float edgeOfDeadZone = targetYaw - Mathf.Sign(error) * _rotationDeadZoneDegrees;
            float nextYaw = Mathf.SmoothDampAngle(
                currentYaw,
                edgeOfDeadZone,
                ref _comfortYawVelocity,
                _comfortYawSmoothTime,
                _maximumComfortYawSpeed,
                deltaTime);
            RotateRigAroundHead(nextYaw);
        }

        private void RotateRigAroundHead(float targetYaw)
        {
            Vector3 pivot = _head != null ? _head.position : _userRig.position;
            RotateRigAroundPivot(targetYaw, pivot);
        }

        private void RotateRigAroundPivot(float targetYaw, Vector3 pivot)
        {
            float yawDelta = Mathf.DeltaAngle(_userRig.eulerAngles.y, targetYaw);
            if (Mathf.Abs(yawDelta) < 0.001f) return;

            Quaternion yawRotation = Quaternion.AngleAxis(yawDelta, Vector3.up);
            _userRig.position = pivot + yawRotation * (_userRig.position - pivot);
            _userRig.rotation = yawRotation * _userRig.rotation;
        }

        private Vector3 GetOperatorPosition()
        {
            return _followAnchor != null
                ? _followAnchor.position
                : transform.TransformPoint(_localOperatorOffset);
        }

        private float GetVehicleFacingYaw()
        {
            if (_followAnchor != null)
                return _followAnchor.eulerAngles.y;

            Quaternion localFacing = Quaternion.Euler(_localFacingEuler);
            return (transform.rotation * localFacing).eulerAngles.y;
        }

        private void IgnorePlayerVehicleCollisions()
        {
            RestorePlayerVehicleCollisions();
            if (_userRig == null) return;

            if (_playerControllers == null || _vehicleColliders == null) return;

            for (int playerIndex = 0; playerIndex < _playerControllers.Length; playerIndex++)
            {
                CharacterController playerController = _playerControllers[playerIndex];
                if (playerController == null) continue;

                for (int vehicleIndex = 0; vehicleIndex < _vehicleColliders.Length; vehicleIndex++)
                {
                    Collider vehicleCollider = _vehicleColliders[vehicleIndex];
                    if (vehicleCollider == null || vehicleCollider == playerController) continue;
                    if (Physics.GetIgnoreCollision(playerController, vehicleCollider)) continue;

                    Physics.IgnoreCollision(playerController, vehicleCollider, true);
                    _ignoredPlayerVehicleCollisions.Add(
                        new IgnoredCollisionPair(playerController, vehicleCollider));
                }
            }
        }

        private void RestorePlayerVehicleCollisions()
        {
            for (int index = 0; index < _ignoredPlayerVehicleCollisions.Count; index++)
            {
                IgnoredCollisionPair pair = _ignoredPlayerVehicleCollisions[index];
                if (pair.PlayerController != null && pair.VehicleCollider != null)
                    Physics.IgnoreCollision(pair.PlayerController, pair.VehicleCollider, false);
            }

            _ignoredPlayerVehicleCollisions.Clear();
        }

        private void EnsureVignette()
        {
            if (!_useTurnVignette || _turnVignetteViewPrefab == null || _head == null) return;
            if (_vignetteInstance != null)
            {
                if (!_vignetteInstance.gameObject.activeSelf)
                    _vignetteInstance.gameObject.SetActive(true);
                return;
            }

            _vignetteInstance = Instantiate(_turnVignetteViewPrefab, _head, false);
            _vignetteInstance.name = "Turn Comfort Vignette";
            _vignetteInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            _vignetteInstance.DisableSampleController();
            _vignetteRenderer = _vignetteInstance.TargetRenderer;
            _vignetteProperties = new MaterialPropertyBlock();
            _currentVignetteAperture = 1f;
            SetVignetteAperture(1f);
        }

        private void UpdateTurnVignette(float vehicleYawSpeed, float deltaTime)
        {
            if (!_useTurnVignette)
            {
                if (_vignetteInstance != null && _vignetteInstance.gameObject.activeSelf)
                    ResetVignetteState(true);
                return;
            }
            if (_vignetteRenderer == null) return;

            float smoothingFactor = 1f - Mathf.Exp(
                -deltaTime / Mathf.Max(0.01f, _vignetteYawSmoothingTime));
            _smoothedVignetteYawSpeed = Mathf.Lerp(
                _smoothedVignetteYawSpeed,
                vehicleYawSpeed,
                smoothingFactor);

            if (_isVignetteEngaged)
            {
                if (_smoothedVignetteYawSpeed <= _vignetteReleaseYawSpeed)
                    _isVignetteEngaged = false;
            }
            else if (_smoothedVignetteYawSpeed >= _vignetteEngageYawSpeed)
            {
                _isVignetteEngaged = true;
            }

            float strength = _isVignetteEngaged
                ? Mathf.Lerp(
                    0.15f,
                    1f,
                    Mathf.InverseLerp(
                        _vignetteEngageYawSpeed,
                        _vignetteFullYawSpeed,
                        _smoothedVignetteYawSpeed))
                : 0f;
            float targetAperture = Mathf.Lerp(1f, _minimumVignetteAperture, strength);
            float easeTime = targetAperture < _currentVignetteAperture
                ? _vignetteEaseInTime
                : _vignetteEaseOutTime;
            _currentVignetteAperture = Mathf.MoveTowards(
                _currentVignetteAperture,
                targetAperture,
                deltaTime / Mathf.Max(0.01f, easeTime));
            SetVignetteAperture(_currentVignetteAperture);
        }

        private void SetVignetteAperture(float aperture)
        {
            if (_vignetteRenderer == null) return;
            if (_vignetteProperties == null) _vignetteProperties = new MaterialPropertyBlock();

            _vignetteRenderer.GetPropertyBlock(_vignetteProperties);
            _vignetteProperties.SetFloat(ApertureSizeId, aperture);
            _vignetteProperties.SetFloat(FeatheringEffectId, _vignetteFeathering);
            _vignetteRenderer.SetPropertyBlock(_vignetteProperties);
        }

        private void ResetVignetteState(bool deactivateInstance)
        {
            _smoothedVignetteYawSpeed = 0f;
            _isVignetteEngaged = false;
            _currentVignetteAperture = 1f;
            SetVignetteAperture(1f);

            if (deactivateInstance && _vignetteInstance != null)
                _vignetteInstance.gameObject.SetActive(false);
        }

        private readonly struct IgnoredCollisionPair
        {
            public IgnoredCollisionPair(
                CharacterController playerController,
                Collider vehicleCollider)
            {
                PlayerController = playerController;
                VehicleCollider = vehicleCollider;
            }

            public CharacterController PlayerController { get; }
            public Collider VehicleCollider { get; }
        }
    }
}
