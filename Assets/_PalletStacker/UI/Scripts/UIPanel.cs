using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public class UIPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("Transition")]
        [SerializeField] private RectTransform _content;
        [SerializeField, Min(0f)] private float _enterDelay = 0.1667f;
        [SerializeField, Min(0f)] private float _fadeDuration = 0.1667f;
        [SerializeField, Min(0f)] private float _settleDuration = 0.3334f;
        [SerializeField, Range(1f, 1.5f)] private float _enterScale = 1.2f;
        [SerializeField, Range(1f, 1.15f)] private float _overshootScale = 1.03f;
        [SerializeField, Range(0.5f, 1f)] private float _exitScale = 0.85f;

        private Sequence _transition;
        private Vector3 _contentBaseScale = Vector3.one;
        private bool _hasContentBaseScale;

        protected CanvasGroup PanelCanvasGroup => _canvasGroup;

        protected virtual void Awake()
        {
            ResolveReferences();
            CacheContentBaseScale();
        }

        protected virtual void OnDisable()
        {
            KillTransition();
        }

        public virtual void Show()
        {
            gameObject.SetActive(true);
            ResolveReferences();
            CacheContentBaseScale();
            KillTransition();
            ResetView();

            CanvasGroup canvasGroup = PanelCanvasGroup;
            if (canvasGroup == null)
            {
                RestoreContentScale();
                return;
            }

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            if (_content != null) _content.localScale = _contentBaseScale * _enterScale;

            _transition = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            _transition.Insert(
                _enterDelay,
                canvasGroup.DOFade(1f, _fadeDuration).SetEase(Ease.Linear));

            if (_content != null)
            {
                _transition.Insert(
                    _enterDelay,
                    _content.DOScale(_contentBaseScale * _overshootScale, _fadeDuration)
                        .SetEase(Ease.OutQuad));
                _transition.Insert(
                    _enterDelay + _fadeDuration,
                    _content.DOScale(_contentBaseScale, _settleDuration)
                        .SetEase(Ease.OutQuad));
            }

            _transition.InsertCallback(_enterDelay + _fadeDuration, () =>
            {
                if (canvasGroup == null) return;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            });
        }

        public virtual void Hide()
        {
            ClearSelection();
            ResolveReferences();
            CacheContentBaseScale();
            KillTransition();

            CanvasGroup canvasGroup = PanelCanvasGroup;
            if (!gameObject.activeInHierarchy || canvasGroup == null)
            {
                HideImmediate();
                return;
            }

            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            _transition = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            _transition.Insert(
                0f,
                canvasGroup.DOFade(0f, _fadeDuration).SetEase(Ease.Linear));
            if (_content != null)
            {
                _transition.Insert(
                    0f,
                    _content.DOScale(
                            _contentBaseScale * _exitScale,
                            _fadeDuration + _enterDelay * 0.5f)
                        .SetEase(Ease.InQuad));
            }

            _transition.OnComplete(() => gameObject.SetActive(false));
        }

        public void HideImmediate()
        {
            ClearSelection();
            ResolveReferences();
            CacheContentBaseScale();
            KillTransition();

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            RestoreContentScale();
            gameObject.SetActive(false);
        }

        public virtual void ResetView()
        {
        }

        private void ResolveReferences()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            if (_content != null) return;

            Transform content = transform.Find("Safe Area/Main Content");
            if (content == null)
            {
                Transform[] children = GetComponentsInChildren<Transform>(true);
                foreach (Transform child in children)
                {
                    if (child.name != "Main Content" && child.name != "Content") continue;
                    content = child;
                    break;
                }
            }

            _content = content as RectTransform;
        }

        private void CacheContentBaseScale()
        {
            if (_hasContentBaseScale || _content == null) return;
            _contentBaseScale = _content.localScale;
            _hasContentBaseScale = true;
        }

        private void RestoreContentScale()
        {
            if (_content != null) _content.localScale = _contentBaseScale;
        }

        private void KillTransition()
        {
            if (_transition != null)
            {
                _transition.Kill();
                _transition = null;
            }

            if (_canvasGroup != null) _canvasGroup.DOKill();
            if (_content != null) _content.DOKill();
        }

        private void ClearSelection()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null) return;
            if (eventSystem.currentSelectedGameObject.transform.IsChildOf(transform))
                eventSystem.SetSelectedGameObject(null);
        }
    }
}
