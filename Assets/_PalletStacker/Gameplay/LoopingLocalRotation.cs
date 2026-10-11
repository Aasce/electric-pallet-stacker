using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class LoopingLocalRotation : MonoBehaviour
    {
        [SerializeField] private Vector3 _localAxis = Vector3.up;
        [SerializeField, Min(0.1f)] private float _secondsPerRevolution = 5f;
        [SerializeField] private bool _resetOnEnable = true;

        private Quaternion _initialLocalRotation;
        private bool _hasInitialRotation;

        private void Awake()
        {
            CacheInitialRotation();
        }

        private void OnEnable()
        {
            CacheInitialRotation();
            if (_resetOnEnable) transform.localRotation = _initialLocalRotation;
        }

        private void Update()
        {
            if (_secondsPerRevolution <= 0f || _localAxis.sqrMagnitude <= 0f) return;

            float degrees = 360f * Time.deltaTime / _secondsPerRevolution;
            transform.Rotate(_localAxis.normalized, degrees, Space.Self);
        }

        private void CacheInitialRotation()
        {
            if (_hasInitialRotation) return;
            _initialLocalRotation = transform.localRotation;
            _hasInitialRotation = true;
        }
    }
}
