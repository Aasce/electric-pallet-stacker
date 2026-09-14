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

        [Header("Lift button local X angles")]
        [SerializeField] private float _liftUpAngle = 10f;
        [SerializeField] private float _liftDownAngle = -10f;

        [Header("Travel button local Z angles")]
        [SerializeField] private float _travelForwardAngle = -45f;
        [SerializeField] private float _travelReverseAngle = 45f;
        [SerializeField, Range(0f, 0.25f)] private float _travelDeadZone = 0.01f;

        [Header("Animation")]
        [Tooltip("On: animate with DOTween. Off: apply the target rotation instantly.")]
        [SerializeField] private bool _useTween = true;
        [SerializeField, Min(0f)] private float _tweenDuration = 0.18f;
        [SerializeField] private Ease _ease = Ease.OutQuad;

        private Quaternion _liftButtonLeftRestRotation;
        private Quaternion _liftButtonRightRestRotation;
        private Quaternion _travelButtonRestRotation;

        private Tween _liftButtonLeftTween;
        private Tween _liftButtonRightTween;
        private Tween _travelButtonTween;
        private PalletStackerLiftState _lastLiftState = PalletStackerLiftState.Neutral;
        private int _lastTravelDirection;

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
            if (command.Lift != _lastLiftState)
            {
                _lastLiftState = command.Lift;
                ApplyLiftPose(LiftAngle(command.Lift));
            }

            int travelDirection = TravelDirection(command.TravelNormalized);
            if (travelDirection != _lastTravelDirection)
            {
                _lastTravelDirection = travelDirection;
                ApplyTravelPose(TravelAngle(travelDirection));
            }
        }

        public void StopImmediately()
        {
            KillTweens();
            SetRotationInstantly(_liftButtonLeft, _liftButtonLeftRestRotation);
            SetRotationInstantly(_liftButtonRight, _liftButtonRightRestRotation);
            SetRotationInstantly(_travelButton, _travelButtonRestRotation);
            _lastLiftState = PalletStackerLiftState.Neutral;
            _lastTravelDirection = 0;
        }

        [ContextMenu("Capture Current Button Pose As Rest Pose")]
        public void CaptureRestPose()
        {
            if (_liftButtonLeft != null)
                _liftButtonLeftRestRotation = _liftButtonLeft.localRotation;
            if (_liftButtonRight != null)
                _liftButtonRightRestRotation = _liftButtonRight.localRotation;
            if (_travelButton != null)
                _travelButtonRestRotation = _travelButton.localRotation;
        }

        private void ApplyLiftPose(float angle)
        {
            AnimateRotation(
                _liftButtonLeft,
                _liftButtonLeftRestRotation * Quaternion.AngleAxis(angle, Vector3.right),
                ref _liftButtonLeftTween);
            AnimateRotation(
                _liftButtonRight,
                _liftButtonRightRestRotation * Quaternion.AngleAxis(angle, Vector3.right),
                ref _liftButtonRightTween);
        }

        private void ApplyTravelPose(float angle)
        {
            AnimateRotation(
                _travelButton,
                _travelButtonRestRotation * Quaternion.AngleAxis(angle, Vector3.forward),
                ref _travelButtonTween);
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
            _liftButtonLeftTween = null;
            _liftButtonRightTween = null;
            _travelButtonTween = null;
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
    }
}
