using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class CrossDirectionPokeTarget : MonoBehaviour
    {
        [SerializeField] private XRBaseInteractable _interactable;
        [SerializeField] private Renderer _targetRenderer;
        [SerializeField] private Color _availableColor = new(0.08f, 0.65f, 1f, 1f);
        [SerializeField] private Color _confirmedColor = new(0.1f, 1f, 0.35f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private CrossSafetyPhaseController _owner;
        private MaterialPropertyBlock _propertyBlock;

        public bool IsConfirmed { get; private set; }

        private void Awake()
        {
            if (_interactable == null) _interactable = GetComponent<XRBaseInteractable>();
            if (_targetRenderer == null) _targetRenderer = GetComponentInChildren<Renderer>(true);
            _propertyBlock = new MaterialPropertyBlock();
            ApplyColor(_availableColor);
        }

        private void OnEnable()
        {
            if (_interactable != null) _interactable.selectEntered.AddListener(HandleSelected);
        }

        private void OnDisable()
        {
            if (_interactable != null) _interactable.selectEntered.RemoveListener(HandleSelected);
        }

        public void Configure(CrossSafetyPhaseController owner)
        {
            _owner = owner;
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
    }
}
