using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    [DisallowMultipleComponent]
    public sealed class AudioSourceFollower : MonoBehaviour
    {
        private Transform _target;
        private Vector3 _offset;
        private bool _hasTarget;

        public bool HasLostTarget => _hasTarget && _target == null;

        public void Follow(Transform target, Vector3 worldOffset)
        {
            _target = target;
            _offset = worldOffset;
            _hasTarget = target != null;
            enabled = _hasTarget;
            SnapToTarget();
        }

        public void Clear()
        {
            _target = null;
            _offset = Vector3.zero;
            _hasTarget = false;
            enabled = false;
        }

        private void LateUpdate()
        {
            SnapToTarget();
        }

        private void SnapToTarget()
        {
            if (_target != null) transform.position = _target.position + _offset;
        }
    }
}
