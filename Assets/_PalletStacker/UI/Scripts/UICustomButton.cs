using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class UICustomButton : MonoBehaviour,
        ISelectHandler,
        IDeselectHandler,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        ISubmitHandler
    {
        [SerializeField] private Button _button;
        [SerializeField] private GameObject _normalLayer;
        [SerializeField] private GameObject _focusedLayer;
        [SerializeField] private TextMeshProUGUI _label;

        [Header("Animation")]
        [SerializeField, Min(0f)] private float _duration = 0.1f;
        [SerializeField] private Ease _ease = Ease.OutQuad;
        [SerializeField, Range(1f, 1.05f)] private float _hoverScale = 1.012f;
        [SerializeField, Range(0.9f, 1f)] private float _pressedScale = 0.985f;
        [SerializeField] private Color _normalTextColor = Color.white;
        [SerializeField] private Color _focusedTextColor = new Color(0f, 0.7843137f, 1f, 1f);
        [SerializeField] private Color _disabledTextColor = new Color(0.66f, 0.78f, 0.84f, 0.5f);

        private CanvasGroup _normalGroup;
        private CanvasGroup _focusedGroup;
        private bool _pointerInside;
        private bool _pointerDown;
        private bool _lastInteractable;
        private Vector3 _baseScale;
        private bool _hasBaseScale;

        public Button Button => _button;
        public TextMeshProUGUI Label => _label;
        public bool IsFocused { get; private set; }

        public event Action<UICustomButton> Focused;

        private void Awake()
        {
            ResolveReferences();
            CacheBaseScale();
            ApplyVisualState(true);
        }

        private void OnEnable()
        {
            ResolveReferences();
            CacheBaseScale();
            IsFocused = EventSystem.current != null &&
                        EventSystem.current.currentSelectedGameObject == gameObject;
            _lastInteractable = _button != null && _button.interactable;
            ApplyVisualState(true);
        }

        private void OnDisable()
        {
            KillTweens();
            _pointerInside = false;
            _pointerDown = false;
        }

        private void Update()
        {
            if (_button == null || _button.interactable == _lastInteractable) return;
            _lastInteractable = _button.interactable;
            ApplyVisualState(false);
        }

        public void OnSelect(BaseEventData eventData)
        {
            SetFocused(true);
            Focused?.Invoke(this);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            SetFocused(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _pointerInside = true;
            ApplyVisualState(false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pointerInside = false;
            _pointerDown = false;
            ApplyVisualState(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_button == null || !_button.interactable) return;
            _pointerDown = true;
            ApplyVisualState(false);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pointerDown = false;
            ApplyVisualState(false);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (_button == null || !_button.interactable) return;
            _pointerDown = true;
            ApplyVisualState(false);
            DOVirtual.DelayedCall(_duration, () =>
                {
                    _pointerDown = false;
                    ApplyVisualState(false);
                }, true)
                .SetLink(gameObject);
        }

        public void SetFocused(bool focused)
        {
            IsFocused = focused;
            ApplyVisualState(false);
        }

        private void ResolveReferences()
        {
            if (_button == null) _button = GetComponent<Button>();
            if (_button != null) _button.transition = Selectable.Transition.None;
            if (_label == null) _label = GetComponentInChildren<TextMeshProUGUI>(true);

            _normalGroup = ResolveGroup(_normalLayer, _normalGroup);
            _focusedGroup = ResolveGroup(_focusedLayer, _focusedGroup);
        }

        private static CanvasGroup ResolveGroup(GameObject layer, CanvasGroup current)
        {
            if (layer == null) return null;
            layer.SetActive(true);
            return current != null
                ? current
                : layer.GetComponent<CanvasGroup>() ?? layer.AddComponent<CanvasGroup>();
        }

        private void ApplyVisualState(bool instant)
        {
            ResolveReferences();
            bool interactable = _button != null && _button.interactable;
            bool highlighted = interactable && (IsFocused || _pointerInside || _pointerDown);

            SetGroupAlpha(_normalGroup, highlighted ? 0f : 1f, instant);
            SetGroupAlpha(_focusedGroup, highlighted ? 1f : 0f, instant);

            float scaleMultiplier = _pointerDown
                ? _pressedScale
                : highlighted ? _hoverScale : 1f;
            AnimateScale(_baseScale * scaleMultiplier, instant);

            Color textColor = !interactable
                ? _disabledTextColor
                : highlighted ? _focusedTextColor : _normalTextColor;
            AnimateTextColor(textColor, instant);
        }

        private void SetGroupAlpha(CanvasGroup group, float alpha, bool instant)
        {
            if (group == null) return;
            group.DOKill();
            group.interactable = false;
            group.blocksRaycasts = false;
            if (instant || _duration <= 0f) group.alpha = alpha;
            else group.DOFade(alpha, _duration).SetEase(_ease).SetUpdate(true).SetLink(gameObject);
        }

        private void AnimateScale(Vector3 target, bool instant)
        {
            transform.DOKill();
            if (instant || _duration <= 0f) transform.localScale = target;
            else
                transform.DOScale(target, _duration)
                    .SetEase(_ease)
                    .SetUpdate(true)
                    .SetLink(gameObject);
        }

        private void AnimateTextColor(Color target, bool instant)
        {
            if (_label == null) return;
            _label.DOKill();
            if (instant || _duration <= 0f) _label.color = target;
            else
                _label.DOColor(target, _duration)
                    .SetEase(_ease)
                    .SetUpdate(true)
                    .SetLink(gameObject);
        }

        private void CacheBaseScale()
        {
            if (_hasBaseScale) return;
            _baseScale = transform.localScale;
            _hasBaseScale = true;
        }

        private void KillTweens()
        {
            transform.DOKill();
            if (_normalGroup != null) _normalGroup.DOKill();
            if (_focusedGroup != null) _focusedGroup.DOKill();
            if (_label != null) _label.DOKill();
        }
    }
}
