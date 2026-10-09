using System;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CrossingVehicleController : MonoBehaviour
    {
        [SerializeField] private Transform _startPoint;
        [SerializeField] private Transform _endPoint;
        [SerializeField, Min(0.1f)] private float _speed = 3f;
        [SerializeField, Min(0.01f)] private float _arrivalDistance = 0.05f;

        private Rigidbody _body;
        private bool _crossing;

        public bool IsCrossing => _crossing;
        public event Action CrossingCompleted;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.useGravity = false;
            ResetVehicle();
        }

        private void FixedUpdate()
        {
            if (!_crossing || _body == null || _endPoint == null) return;
            Vector3 next = Vector3.MoveTowards(
                _body.position,
                _endPoint.position,
                _speed * Time.fixedDeltaTime);
            Vector3 direction = Vector3.ProjectOnPlane(_endPoint.position - _body.position, Vector3.up);
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                _body.MoveRotation(rotation);
            }
            _body.MovePosition(next);
            if ((next - _endPoint.position).sqrMagnitude > _arrivalDistance * _arrivalDistance) return;
            _crossing = false;
            CrossingCompleted?.Invoke();
        }

        public void BeginCrossing()
        {
            if (_crossing || _endPoint == null) return;
            _crossing = true;
        }

        public void ResetVehicle()
        {
            _crossing = false;
            if (_startPoint == null) return;
            if (_body == null) _body = GetComponent<Rigidbody>();
            _body.position = _startPoint.position;
            _body.rotation = _startPoint.rotation;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }
    }
}
