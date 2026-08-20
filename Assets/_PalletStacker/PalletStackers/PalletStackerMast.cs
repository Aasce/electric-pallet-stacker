using System;
using UnityEngine;
namespace ElectricPalletStackers.PalletStackers
{
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
        public float CurrentHeight => forks.localPosition.y;
        public float TargetHeight => Mathf.Lerp(_minHeight, _maxHeight, _targetHeightNormalized);

        private void Update()
        {
            Vector3 position = forks.localPosition;
            position.y = Mathf.MoveTowards(CurrentHeight, TargetHeight, _speed * Time.deltaTime);
            forks.localPosition = position;
        }

        public void SetTargetHeightNormalized(float normalizedHeight) => _targetHeightNormalized = Mathf.Clamp01(normalizedHeight);
        public void SetTargetHeight(float height) => _targetHeightNormalized = Mathf.InverseLerp(_minHeight, _maxHeight, height);

    }
}
