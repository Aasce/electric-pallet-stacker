using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerControlVisuals : MonoBehaviour, IPalletStackerControlOutput
    {
        [Header("Optional model pivots")]
        [SerializeField] private Transform _steeringVisual;
        [SerializeField] private Vector3 _steeringLocalAxis = Vector3.up;
        [SerializeField] private Transform _tillerVisual;
        [SerializeField] private Vector3 _tillerLocalAxis = Vector3.right;

        [Header("Tiller input mapping")]
        [Tooltip("Protocol input at the fully raised/vertical position.")]
        [SerializeField] private float _tillerRaisedInput = 0f;
        [Tooltip("Protocol input at the fully lowered/horizontal position.")]
        [SerializeField] private float _tillerLoweredInput = 100f;
        [SerializeField] private float _tillerRaisedVisualAngle = 0f;
        [SerializeField] private float _tillerLoweredVisualAngle = 90f;

        private Quaternion _steeringRestRotation;
        private Quaternion _tillerRestRotation;

        private void Awake()
        {
            CaptureRestPose();
        }

        public void Apply(PalletStackerDriveCommand command)
        {
            if (_steeringVisual != null)
            {
                _steeringVisual.localRotation = _steeringRestRotation *
                                                Quaternion.AngleAxis(
                                                    command.SteeringDegrees,
                                                    SafeAxis(_steeringLocalAxis, Vector3.up));
            }

            if (_tillerVisual != null)
            {
                float normalized = Mathf.InverseLerp(
                    _tillerRaisedInput,
                    _tillerLoweredInput,
                    command.TillerDegrees);
                float visualAngle = Mathf.Lerp(
                    _tillerRaisedVisualAngle,
                    _tillerLoweredVisualAngle,
                    normalized);
                _tillerVisual.localRotation = _tillerRestRotation *
                                              Quaternion.AngleAxis(
                                                  visualAngle,
                                                  SafeAxis(_tillerLocalAxis, Vector3.right));
            }
        }

        public void StopImmediately()
        {
            if (_steeringVisual != null) _steeringVisual.localRotation = _steeringRestRotation;
            if (_tillerVisual != null) _tillerVisual.localRotation = _tillerRestRotation;
        }

        [ContextMenu("Capture Current Pose As Rest Pose")]
        public void CaptureRestPose()
        {
            if (_steeringVisual != null) _steeringRestRotation = _steeringVisual.localRotation;
            if (_tillerVisual != null) _tillerRestRotation = _tillerVisual.localRotation;
        }

        private static Vector3 SafeAxis(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 0.0001f ? value.normalized : fallback;
        }
    }
}
