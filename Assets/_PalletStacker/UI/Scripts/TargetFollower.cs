using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class TargetFollower : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private bool _isFollowPosition = true;
        [SerializeField] private bool _isFollowRotation = true;
        [SerializeField] private Vector3 _positionOffset = Vector3.zero;

        private void LateUpdate()
        {
            if (_target == null) return;

            if (_isFollowPosition)
            {
                transform.position = _target.TransformPoint(_positionOffset);
            }

            if (_isFollowRotation)
            {
                transform.rotation = _target.rotation;
            }
        }
    }
}
