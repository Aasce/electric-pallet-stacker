using System;
using UnityEngine;
namespace ElectricPalletStackers.PalletStackers
{
    [DisallowMultipleComponent]
    public class PalletStackerMast : MonoBehaviour
    {
        [SerializeField] private Transform forks;

        [Space]
        [SerializeField] private float _minHeight;
        [SerializeField] private float _maxHeight;
        [SerializeField] private float _speed = 1f;

        [SerializeField, Range(0f, 1f)] private float _targetHeightNormalized;

        public event Action<float> OnHeightChanged;

        public Transform Forks => forks;
        public float MinHeight => _minHeight;
        public float MaxHeight => _maxHeight;
        public float Speed => _speed;
        public float CurrentHeight => forks != null ? forks.localPosition.y : _minHeight;
        public float TargetHeight => Mathf.Lerp(_minHeight, _maxHeight, _targetHeightNormalized);
        public float TargetHeightNormalized => _targetHeightNormalized;

        private void Awake()
        {
            if (forks == null)
                Debug.LogError($"{nameof(PalletStackerMast)} on '{name}' requires a forks Transform.", this);
        }

        private void FixedUpdate()
        {
            if (forks == null) return;

            float previousHeight = CurrentHeight;
            Vector3 position = forks.localPosition;
            position.y = Mathf.MoveTowards(CurrentHeight, TargetHeight, _speed * Time.fixedDeltaTime);
            forks.localPosition = position;

            if (!Mathf.Approximately(previousHeight, position.y)) OnHeightChanged?.Invoke(position.y);
        }

        private void OnValidate()
        {
            _speed = Mathf.Max(0f, _speed);
            _maxHeight = Mathf.Max(_minHeight, _maxHeight);
            _targetHeightNormalized = Mathf.Clamp01(_targetHeightNormalized);
        }

        public void SetTargetHeightNormalized(float normalizedHeight) =>
            _targetHeightNormalized = Mathf.Clamp01(normalizedHeight);

        public void SetTargetHeight(float height) =>
            _targetHeightNormalized = Mathf.InverseLerp(_minHeight, _maxHeight, height);

        public void SetLiftDirection(float direction)
        {
            if (direction > 0f) _targetHeightNormalized = 1f;
            else if (direction < 0f) _targetHeightNormalized = 0f;
            else if (forks != null) SetTargetHeight(CurrentHeight);
        }
    }
}
