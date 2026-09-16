using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class UICustomButton : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private GameObject _normalLayer;
        [SerializeField] private GameObject _focusedLayer;
        [SerializeField] private TextMeshProUGUI _label;

        private Button _button;

        public Button Button => _button != null ? _button : (_button = GetComponent<Button>());
        public TextMeshProUGUI Label => _label;
        public bool IsFocused { get; private set; }

        public event Action<UICustomButton> Focused;

        private void Awake()
        {
            Button.transition = Selectable.Transition.None;
            SetFocused(false);
        }

        private void OnEnable()
        {
            SetFocused(EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject);
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

        public void SetFocused(bool focused)
        {
            IsFocused = focused;
            if (_normalLayer != null) _normalLayer.SetActive(!focused);
            if (_focusedLayer != null) _focusedLayer.SetActive(focused);
        }
    }
}
