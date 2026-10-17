using ElectricPalletStackers.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Filtering;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class CrossDirectionPokeTarget : MonoBehaviour, IXRPokeFilter, ILocalizedView
    {
        [SerializeField] private XRBaseInteractable _interactable;
        [SerializeField] private Renderer _targetRenderer;
        [SerializeField] private Color _availableColor = new(0.08f, 0.65f, 1f, 1f);
        [SerializeField] private Color _confirmedColor = new(0.1f, 1f, 0.35f, 1f);
        [Tooltip("Fingertip distance from the sphere surface that counts as a poke, in metres.")]
        [SerializeField, Range(0f, 0.015f)] private float _pokeContactTolerance = 0.005f;

        [Header("Instruction label")]
        [SerializeField] private string _instructionLocalizationKey =
            LocalizationKeys.CrossSafety.PointThisWay;
        [SerializeField, Min(0f)] private float _labelHeight = 0.14f;
        [SerializeField] private Vector2 _labelSize = new(420f, 88f);
        [SerializeField, Min(0.0001f)] private float _labelWorldScale = 0.001f;
        [SerializeField, Min(1f)] private float _labelFontSize = 30f;
        [SerializeField] private Color _labelTextColor = Color.white;
        [SerializeField] private Color _labelBackgroundColor = new(0.015f, 0.045f, 0.08f, 0.94f);
        [SerializeField] private Color _labelFrameColor = new(0.08f, 0.65f, 1f, 1f);
        [SerializeField, Min(0f)] private float _labelFrameThickness = 4f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private const string AlwaysOnTopUiShaderName =
            "Electric Pallet Stackers/UI/Always On Top";
        private const string AlwaysOnTopTextShaderName =
            "TextMeshPro/Distance Field Overlay";

        private CrossSafetyPhaseController _owner;
        private MaterialPropertyBlock _propertyBlock;
        private Collider[] _interactionColliders;
        private Collider[] _ignoredVehicleColliders;
        private RectTransform _instructionLabelRoot;
        private TMP_Text _instructionLabelText;
        private TMP_FontAsset _defaultInstructionFont;
        private Material _alwaysOnTopUiMaterial;
        private Material _alwaysOnTopTextMaterial;

        public bool IsConfirmed { get; private set; }
        public bool canProcess => isActiveAndEnabled;

        private void Awake()
        {
            if (_interactable == null) _interactable = GetComponent<XRBaseInteractable>();
            if (_targetRenderer == null) _targetRenderer = GetComponentInChildren<Renderer>(true);
            CacheInteractionColliders();
            _propertyBlock = new MaterialPropertyBlock();
            ApplyColor(_availableColor);
            EnsureInstructionLabel();
            SetInstructionVisible(false);
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

            LocalizationManager localizationManager = FindFirstObjectByType<LocalizationManager>();
            localizationManager?.Register(this);
        }

        private void OnDisable()
        {
            if (_interactable != null)
            {
                _interactable.selectFilters.Remove(this);
                _interactable.interactionStrengthFilters.Remove(this);
                _interactable.selectEntered.RemoveListener(HandleSelected);
            }

            LocalizationManager localizationManager = FindFirstObjectByType<LocalizationManager>();
            localizationManager?.Unregister(this);
        }

        private void OnDestroy()
        {
            DestroyRuntimeMaterial(_alwaysOnTopUiMaterial);
            DestroyRuntimeMaterial(_alwaysOnTopTextMaterial);
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
            SetInstructionVisible(false);
        }

        public void SetInstructionVisible(bool visible)
        {
            EnsureInstructionLabel();
            if (_instructionLabelRoot != null)
                _instructionLabelRoot.gameObject.SetActive(visible);
        }

        public void FaceInstructionLabel(Transform viewer)
        {
            if (viewer == null) return;
            EnsureInstructionLabel();
            if (_instructionLabelRoot == null ||
                !_instructionLabelRoot.gameObject.activeSelf) return;

            _instructionLabelRoot.position = transform.position + Vector3.up * _labelHeight;
            Vector3 directionToViewer = Vector3.ProjectOnPlane(
                viewer.position - _instructionLabelRoot.position,
                Vector3.up);
            if (directionToViewer.sqrMagnitude > 0.0001f)
                _instructionLabelRoot.rotation = Quaternion.LookRotation(
                    -directionToViewer.normalized,
                    Vector3.up);
        }

        public void ApplyLocalization(ILocalizationService localization)
        {
            EnsureInstructionLabel();
            if (_instructionLabelText == null) return;

            if (localization == null)
            {
                _instructionLabelText.text = "Point in this direction";
                ApplyAlwaysOnTopTextMaterial();
                return;
            }

            _instructionLabelText.text = localization.GetText(_instructionLocalizationKey);
            TMP_FontAsset fontOverride = localization.GetFontOverride();
            _instructionLabelText.font = fontOverride != null
                ? fontOverride
                : _defaultInstructionFont;
            ApplyAlwaysOnTopTextMaterial();
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

        private void EnsureInstructionLabel()
        {
            if (_instructionLabelRoot != null && _instructionLabelText != null) return;

            Transform existing = transform.Find("Direction Instruction Label");
            if (existing != null)
            {
                _instructionLabelRoot = existing as RectTransform;
                _instructionLabelText = existing.GetComponentInChildren<TMP_Text>(true);
                if (_instructionLabelText != null)
                    _defaultInstructionFont = _instructionLabelText.font;
                return;
            }

            GameObject canvasObject = new("Direction Instruction Label",
                typeof(RectTransform),
                typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            _instructionLabelRoot = canvasObject.GetComponent<RectTransform>();
            _instructionLabelRoot.sizeDelta = _labelSize;
            SetLabelWorldScale();

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;

            Shader alwaysOnTopUiShader = Shader.Find(AlwaysOnTopUiShaderName);
            if (alwaysOnTopUiShader != null)
                _alwaysOnTopUiMaterial = new Material(alwaysOnTopUiShader)
                {
                    name = "Direction Label UI Always On Top (Runtime)"
                };

            RectTransform frame = CreateImage(
                "Frame",
                _instructionLabelRoot,
                Vector2.zero,
                _labelFrameColor,
                _alwaysOnTopUiMaterial);
            RectTransform background = CreateImage(
                "Background",
                frame,
                new Vector2(_labelFrameThickness, _labelFrameThickness),
                _labelBackgroundColor,
                _alwaysOnTopUiMaterial);

            GameObject textObject = new("Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(background, false);
            StretchRect(textRect, new Vector2(14f, 6f));

            _instructionLabelText = textObject.GetComponent<TextMeshProUGUI>();
            _instructionLabelText.text = "Point in this direction";
            _instructionLabelText.fontSize = _labelFontSize;
            _instructionLabelText.color = _labelTextColor;
            _instructionLabelText.alignment = TextAlignmentOptions.Center;
            _instructionLabelText.textWrappingMode = TextWrappingModes.NoWrap;
            _instructionLabelText.raycastTarget = false;
            _defaultInstructionFont = _instructionLabelText.font;
            ApplyAlwaysOnTopTextMaterial();
        }

        private void ApplyAlwaysOnTopTextMaterial()
        {
            if (_instructionLabelText == null ||
                _instructionLabelText.fontSharedMaterial == null) return;

            Shader overlayShader = Shader.Find(AlwaysOnTopTextShaderName);
            if (overlayShader == null) return;

            if (_alwaysOnTopTextMaterial != null)
                DestroyRuntimeMaterial(_alwaysOnTopTextMaterial);
            _alwaysOnTopTextMaterial = new Material(_instructionLabelText.fontSharedMaterial)
            {
                name = "Direction Label Text Always On Top (Runtime)",
                shader = overlayShader
            };
            _instructionLabelText.fontSharedMaterial = _alwaysOnTopTextMaterial;
        }

        private static void DestroyRuntimeMaterial(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }

        private void SetLabelWorldScale()
        {
            if (_instructionLabelRoot == null) return;

            Vector3 lossyScale = transform.lossyScale;
            float parentScale = Mathf.Max(
                Mathf.Abs(lossyScale.x),
                Mathf.Abs(lossyScale.y),
                Mathf.Abs(lossyScale.z));
            if (parentScale < 0.0001f) parentScale = 1f;
            _instructionLabelRoot.localScale = Vector3.one * (_labelWorldScale / parentScale);
        }

        private static RectTransform CreateImage(
            string name,
            RectTransform parent,
            Vector2 inset,
            Color color,
            Material material)
        {
            GameObject imageObject = new(name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            StretchRect(rect, inset);

            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.material = material;
            image.raycastTarget = false;
            return rect;
        }

        private static void StretchRect(RectTransform rect, Vector2 inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = inset;
            rect.offsetMax = -inset;
        }

        private void CacheInteractionColliders()
        {
            if (_interactionColliders == null || _interactionColliders.Length == 0)
                _interactionColliders = GetComponentsInChildren<Collider>(true);
        }
    }
}
