using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Filtering;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class CrossDirectionPokeTarget : MonoBehaviour, IXRPokeFilter
    {
        [SerializeField] private XRBaseInteractable _interactable;
        [SerializeField] private Renderer _targetRenderer;
        [SerializeField] private Color _availableColor = new(0.08f, 0.65f, 1f, 1f);
        [SerializeField] private Color _confirmedColor = new(0.1f, 1f, 0.35f, 1f);
        [Tooltip("Fingertip distance from the sphere surface that counts as a poke, in metres.")]
        [SerializeField, Range(0f, 0.015f)] private float _pokeContactTolerance = 0.005f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private CrossSafetyPhaseController _owner;
        private MaterialPropertyBlock _propertyBlock;
        private Collider[] _interactionColliders;
        private Collider[] _ignoredVehicleColliders;

        public bool IsConfirmed { get; private set; }
        public bool canProcess => isActiveAndEnabled;

        private void Awake()
        {
            if (_interactable == null) _interactable = GetComponent<XRBaseInteractable>();
            if (_targetRenderer == null) _targetRenderer = GetComponentInChildren<Renderer>(true);
            CacheInteractionColliders();
            _propertyBlock = new MaterialPropertyBlock();
            ApplyColor(_availableColor);
        }

        private void OnEnable()
        {
            if (_interactable != null)
            {
                _interactable.selectFilters.Add(this);
                _interactable.interactionStrengthFilters.Add(this);
                _interactable.selectEntered.AddListener(HandleSelected);
            }
            ConfigureVehicleCollisionPairs();
        }

        private void OnDisable()
        {
            if (_interactable != null)
            {
                _interactable.selectFilters.Remove(this);
                _interactable.interactionStrengthFilters.Remove(this);
                _interactable.selectEntered.RemoveListener(HandleSelected);
            }
        }

        public void Configure(CrossSafetyPhaseController owner, Collider[] ignoredVehicleColliders)
        {
            _owner = owner;
            _ignoredVehicleColliders = ignoredVehicleColliders;
            ConfigureVehicleCollisionPairs();
        }

        private void ConfigureVehicleCollisionPairs()
        {
            CacheInteractionColliders();

            for (int interactionIndex = 0;
                 interactionIndex < _interactionColliders.Length;
                 interactionIndex++)
            {
                Collider interactionCollider = _interactionColliders[interactionIndex];
                if (interactionCollider == null) continue;

                // Poke interactors ignore trigger colliders. Reapply these pairs whenever
                // targets are enabled, since the vehicle and targets are reset separately.
                interactionCollider.isTrigger = false;
                if (_ignoredVehicleColliders == null) continue;

                for (int vehicleIndex = 0;
                     vehicleIndex < _ignoredVehicleColliders.Length;
                     vehicleIndex++)
                {
                    Collider vehicleCollider = _ignoredVehicleColliders[vehicleIndex];
                    if (vehicleCollider == null || vehicleCollider == interactionCollider) continue;
                    Physics.IgnoreCollision(interactionCollider, vehicleCollider, true);
                }
            }
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            return interactor is XRPokeInteractor && IsTouchingSurface(interactor);
        }

        public float Process(IXRInteractor interactor, IXRInteractable interactable, float interactionStrength)
        {
            return interactor is XRPokeInteractor && IsTouchingSurface(interactor) ? 1f : 0f;
        }

        private bool IsTouchingSurface(IXRInteractor interactor)
        {
            CacheInteractionColliders();
            Vector3 fingertip = interactor.GetAttachTransform(_interactable).position;
            float toleranceSquared = _pokeContactTolerance * _pokeContactTolerance;
            for (int index = 0; index < _interactionColliders.Length; index++)
            {
                Collider targetCollider = _interactionColliders[index];
                if (targetCollider == null || !targetCollider.enabled ||
                    !targetCollider.gameObject.activeInHierarchy) continue;
                if ((targetCollider.ClosestPoint(fingertip) - fingertip).sqrMagnitude <= toleranceSquared)
                    return true;
            }
            return false;
        }

        public void ResetTarget()
        {
            IsConfirmed = false;
            EnsureRendererCache();
            ApplyColor(_availableColor);
        }

        private void HandleSelected(SelectEnterEventArgs args)
        {
            if (IsConfirmed || args.interactorObject is not XRPokeInteractor) return;

            IsConfirmed = true;
            ApplyColor(_confirmedColor);
            _owner?.NotifyDirectionConfirmed(this);
        }

        private void ApplyColor(Color color)
        {
            if (_targetRenderer == null) return;
            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();

            _targetRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            _targetRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void EnsureRendererCache()
        {
            if (_interactable == null) _interactable = GetComponent<XRBaseInteractable>();
            if (_targetRenderer == null) _targetRenderer = GetComponentInChildren<Renderer>(true);
            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();
        }

        private void CacheInteractionColliders()
        {
            if (_interactionColliders == null || _interactionColliders.Length == 0)
                _interactionColliders = GetComponentsInChildren<Collider>(true);
        }
    }
}
