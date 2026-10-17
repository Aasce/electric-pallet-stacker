using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class TargetFollower : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private bool _isFollowPosition = true;
        [SerializeField] private bool _isFollowRotation = true;
        [Tooltip("Follow only the target's heading around world Y, keeping this object upright.")]
        [SerializeField] private bool _isFollowYawOnly;
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
                if (_isFollowYawOnly)
                {
                    Vector3 planarForward = Vector3.ProjectOnPlane(_target.forward, Vector3.up);
                    if (planarForward.sqrMagnitude > 0.0001f)
                        transform.rotation = Quaternion.LookRotation(planarForward, Vector3.up);
                }
                else
                {
                    transform.rotation = _target.rotation;
                }
            }
        }
    }
}
