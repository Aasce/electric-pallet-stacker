using DG.Tweening;
using ElectricPalletStackers.Ble;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerButtonVisuals : MonoBehaviour, IPalletStackerControlOutput
    {
        [Header("Model pivots")]
        [SerializeField] private Transform _liftButtonLeft;
        [SerializeField] private Transform _liftButtonRight;
        [SerializeField] private Transform _travelButton;
        [SerializeField] private Transform _emergencyStopButton;
        [SerializeField] private Transform _hornButton;
        [SerializeField] private Transform _slowModeButton;

        [Header("Lift button local X angles")]
        [SerializeField] private float _liftUpAngle = 10f;
        [SerializeField] private float _liftDownAngle = -10f;

        [Header("Travel button local Z angles")]
        [SerializeField] private float _travelForwardAngle = -45f;
        [SerializeField] private float _travelReverseAngle = 45f;
        [SerializeField, Range(0f, 0.25f)] private float _travelDeadZone = 0.01f;

        [Header("Button press depth")]
        [SerializeField] private Vector3 _pressLocalDirection = Vector3.down;
        [SerializeField, Min(0f)] private float _pressDistance = 0.008f;

        [Header("Animation")]
        [Tooltip("On: animate with DOTween. Off: apply the target rotation instantly.")]
        [SerializeField] private bool _useTween = true;
        [SerializeField, Min(0f)] private float _tweenDuration = 0.18f;
        [SerializeField] private Ease _ease = Ease.OutQuad;

        private Quaternion _liftButtonLeftRestRotation;
        private Quaternion _liftButtonRightRestRotation;
        private Quaternion _travelButtonRestRotation;
        private Vector3 _travelButtonRestPosition;
        private Vector3 _emergencyStopRestPosition;
        private Vector3 _hornButtonRestPosition;
        private Vector3 _slowModeRestPosition;

        private Tween _liftButtonLeftTween;
        private Tween _liftButtonRightTween;
        private Tween _travelButtonTween;
        private Tween _travelButtonPressTween;
        private Tween _emergencyStopPressTween;
        private Tween _hornPressTween;
        private Tween _slowModePressTween;
        private PalletStackerLiftState _lastLiftState = PalletStackerLiftState.Neutral;
        private int _lastTravelDirection;
        private bool _lastEmergencyStop;
        private bool _lastHorn;
        private bool _lastSlowMode;

        private void Awake()
        {
            CaptureRestPose();
        }

        private void OnDisable()
        {
            StopImmediately();
        }

        public void Apply(PalletStackerDriveCommand command)
        {
            if (command.LiftInput != _lastLiftState)
            {
                _lastLiftState = command.LiftInput;
                ApplyLiftPose(LiftAngle(command.LiftInput));
            }

            int travelDirection = TravelDirection(command.TravelInputNormalized);
            if (travelDirection != _lastTravelDirection)
            {
                _lastTravelDirection = travelDirection;
                ApplyTravelPose(TravelAngle(travelDirection));
                ApplyButtonPress(_travelButton, _travelButtonRestPosition, travelDirection != 0, ref _travelButtonPressTween);
            }

            if (command.EmergencyStop != _lastEmergencyStop)
            {
                _lastEmergencyStop = command.EmergencyStop;
                ApplyButtonPress(_emergencyStopButton, _emergencyStopRestPosition, command.EmergencyStop, ref _emergencyStopPressTween);
            }

            if (command.Horn != _lastHorn)
            {
                _lastHorn = command.Horn;
                ApplyButtonPress(_hornButton, _hornButtonRestPosition, command.Horn, ref _hornPressTween);
            }

            if (command.SlowMode != _lastSlowMode)
            {
                _lastSlowMode = command.SlowMode;
                ApplyButtonPress(_slowModeButton, _slowModeRestPosition, command.SlowMode, ref _slowModePressTween);
            }
        }

        public void StopImmediately()
        {
            KillTweens();
            SetRotationInstantly(_liftButtonLeft, _liftButtonLeftRestRotation);
            SetRotationInstantly(_liftButtonRight, _liftButtonRightRestRotation);
            SetRotationInstantly(_travelButton, _travelButtonRestRotation);
            SetPositionInstantly(_travelButton, _travelButtonRestPosition);
            SetPositionInstantly(_emergencyStopButton, _emergencyStopRestPosition);
            SetPositionInstantly(_hornButton, _hornButtonRestPosition);
            SetPositionInstantly(_slowModeButton, _slowModeRestPosition);
            _lastLiftState = PalletStackerLiftState.Neutral;
            _lastTravelDirection = 0;
            _lastEmergencyStop = false;
            _lastHorn = false;
            _lastSlowMode = false;
        }

        [ContextMenu("Capture Current Button Pose As Rest Pose")]
        public void CaptureRestPose()
        {
            if (_liftButtonLeft != null)
                _liftButtonLeftRestRotation = _liftButtonLeft.localRotation;
            if (_liftButtonRight != null)
                _liftButtonRightRestRotation = _liftButtonRight.localRotation;
            if (_travelButton != null)
            {
                _travelButtonRestRotation = _travelButton.localRotation;
                _travelButtonRestPosition = _travelButton.localPosition;
            }
            if (_emergencyStopButton != null) _emergencyStopRestPosition = _emergencyStopButton.localPosition;
            if (_hornButton != null) _hornButtonRestPosition = _hornButton.localPosition;
            if (_slowModeButton != null) _slowModeRestPosition = _slowModeButton.localPosition;
        }

        private void ApplyLiftPose(float angle)
        {
            AnimateRotation(
                _liftButtonLeft,
                _liftButtonLeftRestRotation * Quaternion.AngleAxis(angle, Vector3.right),
                ref _liftButtonLeftTween);
            AnimateRotation(
                _liftButtonRight,
                _liftButtonRightRestRotation * Quaternion.AngleAxis(-angle, Vector3.right),
                ref _liftButtonRightTween);
        }

        private void ApplyTravelPose(float angle)
        {
            AnimateRotation(
                _travelButton,
                _travelButtonRestRotation * Quaternion.AngleAxis(angle, Vector3.forward),
                ref _travelButtonTween);
        }

        private void ApplyButtonPress(Transform target, Vector3 restPosition, bool pressed, ref Tween tween)
        {
            Vector3 direction = _pressLocalDirection.sqrMagnitude > 0.0001f
                ? _pressLocalDirection.normalized
                : Vector3.down;
            Vector3 targetPosition = restPosition + (pressed ? direction * _pressDistance : Vector3.zero);
            tween?.Kill();
            tween = null;
            if (target == null) return;
            if (!_useTween || _tweenDuration <= 0f)
            {
                target.localPosition = targetPosition;
                return;
            }

            tween = target
                .DOLocalMove(targetPosition, _tweenDuration)
                .SetEase(_ease)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        private void AnimateRotation(Transform target, Quaternion targetRotation, ref Tween tween)
        {
            tween?.Kill();
            tween = null;

            if (target == null) return;
            if (!_useTween || _tweenDuration <= 0f)
            {
                target.localRotation = targetRotation;
                return;
            }

            tween = target
                .DOLocalRotateQuaternion(targetRotation, _tweenDuration)
                .SetEase(_ease)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        private void KillTweens()
        {
            _liftButtonLeftTween?.Kill();
            _liftButtonRightTween?.Kill();
            _travelButtonTween?.Kill();
            _travelButtonPressTween?.Kill();
            _emergencyStopPressTween?.Kill();
            _hornPressTween?.Kill();
            _slowModePressTween?.Kill();
            _liftButtonLeftTween = null;
            _liftButtonRightTween = null;
            _travelButtonTween = null;
            _travelButtonPressTween = null;
            _emergencyStopPressTween = null;
            _hornPressTween = null;
            _slowModePressTween = null;
        }

        private float LiftAngle(PalletStackerLiftState lift)
        {
            return lift switch
            {
                PalletStackerLiftState.Up => _liftUpAngle,
                PalletStackerLiftState.Down => _liftDownAngle,
                _ => 0f
            };
        }

        private float TravelAngle(int direction)
        {
            if (direction > 0) return _travelForwardAngle;
            if (direction < 0) return _travelReverseAngle;
            return 0f;
        }

        private int TravelDirection(float travel)
        {
            if (travel > _travelDeadZone) return 1;
            if (travel < -_travelDeadZone) return -1;
            return 0;
        }

        private static void SetRotationInstantly(Transform target, Quaternion rotation)
        {
            if (target != null) target.localRotation = rotation;
        }

        private static void SetPositionInstantly(Transform target, Vector3 position)
        {
            if (target != null) target.localPosition = position;
        }
    }
}
