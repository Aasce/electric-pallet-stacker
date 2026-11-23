using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class ManagerHoverReveal : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GameObject _revealedButton;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_revealedButton != null && !_revealedButton.activeSelf)
                _revealedButton.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_revealedButton != null && _revealedButton.activeSelf)
                _revealedButton.SetActive(false);
        }

        private void OnDisable()
        {
            if (_revealedButton != null && _revealedButton.activeSelf)
                _revealedButton.SetActive(false);
        }
    }
}
