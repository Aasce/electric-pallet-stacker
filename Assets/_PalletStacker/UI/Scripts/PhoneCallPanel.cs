using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class PhoneCallPanel : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _content;
        [SerializeField] private TMP_Text _statusLabel;
        [SerializeField] private TMP_Text _callerLabel;
        [SerializeField] private TMP_Text _hintLabel;
        [SerializeField] private GameObject _actions;
        [SerializeField] private Button _rejectButton;
        [SerializeField] private Button _acceptButton;

        [Header("Placement")]
        [Tooltip("Defaults to the main camera when left empty.")]
        [SerializeField] private Transform _viewer;
        [SerializeField] private Vector3 _viewerOffset = new(0.12f, -0.03f, 0.55f);

        [Header("Presentation")]
        [SerializeField] private string _defaultCaller = "0123456789";
        [SerializeField, Min(0f)] private float _transitionDuration = 0.2f;
        [SerializeField] private bool _startHidden = true;

        [Header("Optional Audio (assign later)")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _ringtoneClip;
        [SerializeField] private AudioClip _conversationClip;

        private Vector3 _baseScale = Vector3.one;
        private bool _isInitialized;

        public bool IsRinging { get; private set; }
        public string CallerDisplay => _callerLabel != null ? _callerLabel.text : string.Empty;

        public event Action CallAccepted;
        public event Action CallRejected;

        private void Awake()
        {
            Initialize();
            if (_startHidden) HideImmediate();
        }

        private void OnDestroy()
        {
            if (_rejectButton != null) _rejectButton.onClick.RemoveListener(RejectCall);
            if (_acceptButton != null) _acceptButton.onClick.RemoveListener(AcceptCall);
            KillTweens();
        }

        [ContextMenu("Simulate Incoming Call")]
        public void ShowDefaultIncomingCall()
        {
            ShowIncomingCall(_defaultCaller);
        }

        public void ShowIncomingCall(string callerDisplay)
        {
            Initialize();
            SetCallerDisplay(string.IsNullOrWhiteSpace(callerDisplay) ? _defaultCaller : callerDisplay);
            PlaceInFrontOfViewer();

            IsRinging = true;
            SetRingingVisual();
            PlayClip(_ringtoneClip, true);
            KillTweens();

            if (_canvasGroup == null) return;

            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;

            if (_content != null) _content.localScale = _baseScale * 0.92f;
            _canvasGroup.DOFade(1f, _transitionDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(gameObject);
            if (_content != null)
            {
                _content.DOScale(_baseScale, _transitionDuration)
                    .SetEase(Ease.OutBack)
                    .SetUpdate(true)
                    .SetLink(gameObject);
            }
        }

        public void SetCallerDisplay(string callerDisplay)
        {
            if (_callerLabel != null) _callerLabel.text = callerDisplay;
        }

        public void AcceptCall()
        {
            if (!IsRinging) return;
            IsRinging = false;
            SetActiveCallVisual();
            PlayClip(_conversationClip, false);
            CallAccepted?.Invoke();
        }

        public void RejectCall()
        {
            if (!IsRinging) return;
            IsRinging = false;
            CallRejected?.Invoke();
            Hide();
        }

        public void FinishCall()
        {
            Hide();
        }

        public void Hide()
        {
            Initialize();
            IsRinging = false;
            KillTweens();
            StopAudio();

            if (_canvasGroup == null) return;

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.DOFade(0f, _transitionDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .SetLink(gameObject);
            if (_content != null)
            {
                _content.DOScale(_baseScale * 0.94f, _transitionDuration)
                    .SetEase(Ease.InQuad)
                    .SetUpdate(true)
                    .SetLink(gameObject);
            }
        }

        public void HideImmediate()
        {
            Initialize();
            IsRinging = false;
            KillTweens();
            StopAudio();

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            if (_content != null) _content.localScale = _baseScale;
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            if (_content == null) _content = transform as RectTransform;
            if (_callerLabel == null)
            {
                Transform caller = transform.Find("Background/Caller Number");
                if (caller != null) _callerLabel = caller.GetComponent<TMP_Text>();
            }

            _baseScale = _content != null ? _content.localScale : transform.localScale;

            if (_rejectButton != null) _rejectButton.onClick.AddListener(RejectCall);
            if (_acceptButton != null) _acceptButton.onClick.AddListener(AcceptCall);

            _isInitialized = true;
        }

        private void PlaceInFrontOfViewer()
        {
            if (_viewer == null && Camera.main != null) _viewer = Camera.main.transform;
            if (_viewer == null) return;

            transform.position = _viewer.TransformPoint(_viewerOffset);

            Vector3 forward = Vector3.ProjectOnPlane(_viewer.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = _viewer.forward;
            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void SetRingingVisual()
        {
            if (_statusLabel != null) _statusLabel.text = "CUỘC GỌI ĐẾN";
            if (_hintLabel != null) _hintLabel.text = "Chạm để trả lời";
            if (_actions != null) _actions.SetActive(true);
        }

        private void SetActiveCallVisual()
        {
            if (_statusLabel != null) _statusLabel.text = "ĐANG TRONG CUỘC GỌI";
            if (_hintLabel != null) _hintLabel.text = "Giữ xe đứng yên";
            if (_actions != null) _actions.SetActive(false);
        }

        private void PlayClip(AudioClip clip, bool loop)
        {
            if (_audioSource == null || clip == null) return;

            _audioSource.Stop();
            _audioSource.clip = clip;
            _audioSource.loop = loop;
            _audioSource.Play();
        }

        private void StopAudio()
        {
            if (_audioSource == null) return;
            _audioSource.Stop();
            _audioSource.clip = null;
            _audioSource.loop = false;
        }

        private void KillTweens()
        {
            if (_canvasGroup != null) _canvasGroup.DOKill();
            if (_content != null) _content.DOKill();
        }
    }
}
