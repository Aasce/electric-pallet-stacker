using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public class UIPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;

        protected CanvasGroup PanelCanvasGroup
        {
            get
            {
                if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
                return _canvasGroup;
            }
        }

        public virtual void Show()
        {
            gameObject.SetActive(true);
            ResetView();

            CanvasGroup canvasGroup = PanelCanvasGroup;
            if (canvasGroup == null) return;
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        public virtual void Hide()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null &&
                eventSystem.currentSelectedGameObject != null &&
                eventSystem.currentSelectedGameObject.transform.IsChildOf(transform))
            {
                eventSystem.SetSelectedGameObject(null);
            }

            CanvasGroup canvasGroup = PanelCanvasGroup;
            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            gameObject.SetActive(false);
        }

        public virtual void ResetView()
        {
        }
    }
}
