using System;
using DG.Tweening;
using ElectricPalletStackers.Localization;
using ElectricPalletStackers.PalletStackers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    public enum PhoneCallState
    {
        Hidden,
        Ringing,
        InCall
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    [RequireComponent(typeof(WorldSpacePanelGrab))]
    public sealed class PhoneCallPanel : MonoBehaviour, ILocalizedView
    {
        [Header("Content")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _content;
        [SerializeField] private TMP_Text _statusLabel;
        [SerializeField] private TMP_Text _callerLabel;
        [SerializeField] private TMP_Text _hintLabel;
        [SerializeField] private GameObject _incomingActions;
        [SerializeField] private GameObject _activeCallActions;
        [SerializeField] private Button _rejectButton;
        [SerializeField] private Button _acceptButton;
        [SerializeField] private Button _hangUpButton;

        [Header("Hand Interaction")]
        [SerializeField] private WorldSpacePanelGrab _panelGrab;
        [SerializeField] private Collider _moveCollider;

        [Header("Placement")]
        [Tooltip("Defaults to the main camera when left empty.")]
        [SerializeField] private Transform _viewer;
        [SerializeField] private Vector3 _viewerOffset = new(-0.06f, -0.03f, 0.55f);

        [Header("Presentation")]
        [SerializeField] private string _defaultCaller = "0123456789";
        [SerializeField, Min(0f)] private float _transitionDuration = 0.2f;
        [SerializeField] private bool _startHidden = true;

        private Vector3 _baseScale = Vector3.one;
        private bool _isInitialized;
        private float _callStartedAt;
        private int _lastDisplayedCallSecond = -1;
        private ILocalizationService _localization;
        private TMP_FontAsset _statusDefaultFont;
        private TMP_FontAsset _hintDefaultFont;
        private bool _followViewerPosition;

        public bool IsRinging { get; private set; }
        public bool IsInCall { get; private set; }
        public PhoneCallState State { get; private set; }
        public string CallerDisplay => _callerLabel != null ? _callerLabel.text : string.Empty;

        public event Action CallAccepted;
        public event Action CallRejected;
        public event Action CallEndedByUser;
        public event Action<PhoneCallState, PhoneCallState> StateChanged;

        public void ApplyLocalization(ILocalizationService localization)
        {
            _localization = localization;
            CacheLocalizedFonts();
            ApplyLocalizedFonts();

            if (IsInCall)
            {
                SetLocalizedText(_statusLabel, LocalizationKeys.Phone.InCall, "In call");
                if (_lastDisplayedCallSecond >= 0) UpdateCallDuration(_lastDisplayedCallSecond);
            }
            else
            {
                SetLocalizedText(_statusLabel, LocalizationKeys.Phone.IncomingCall, "Incoming call");
                SetLocalizedText(_hintLabel, LocalizationKeys.Phone.TapToAnswer, "Tap to answer");
            }
        }

        private void Awake()
        {
            Initialize();
            if (_startHidden) HideImmediate();
        }

        private void OnDestroy()
        {
            if (_rejectButton != null) _rejectButton.onClick.RemoveListener(RejectCall);
            if (_acceptButton != null) _acceptButton.onClick.RemoveListener(AcceptCall);
            if (_hangUpButton != null) _hangUpButton.onClick.RemoveListener(HangUpCall);
            KillTweens();
        }

        private void Update()
        {
            if (!IsInCall) return;

            int elapsedSeconds = Mathf.Max(0, Mathf.FloorToInt(Time.realtimeSinceStartup - _callStartedAt));
            if (elapsedSeconds == _lastDisplayedCallSecond) return;

            _lastDisplayedCallSecond = elapsedSeconds;
            UpdateCallDuration(elapsedSeconds);
        }

        private void LateUpdate()
        {
            if (!_followViewerPosition) return;
            if (!TryResolveViewer()) return;

            // Let the hand move the panel freely. While it is held, keep the latest
            // camera-relative offset so releasing it does not snap it back.
            if (_panelGrab != null && _panelGrab.IsGrabbed)
            {
                _viewerOffset = _viewer.InverseTransformPoint(transform.position);
                return;
            }

            // Position follows the headset, while the panel keeps its own rotation.
            transform.position = _viewer.TransformPoint(_viewerOffset);
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
            // The UI hierarchy follows the vehicle. A call panel must be independent so
            // it stays at the viewer position and can be moved by hand after appearing.
            transform.SetParent(null, true);
            PlaceInFrontOfViewer();
            _followViewerPosition = true;

            IsRinging = true;
            IsInCall = false;
            SetState(PhoneCallState.Ringing);
            SetMoveInteractionEnabled(true);
            SetRingingVisual();
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
            IsInCall = true;
            SetState(PhoneCallState.InCall);
            _callStartedAt = Time.realtimeSinceStartup;
            _lastDisplayedCallSecond = -1;
            SetActiveCallVisual();
            CallAccepted?.Invoke();
        }

        public void RejectCall()
        {
            if (!IsRinging) return;
            IsRinging = false;
            CallRejected?.Invoke();
            Hide();
        }

        public void HangUpCall()
        {
            if (!IsInCall) return;

            IsInCall = false;
            CallEndedByUser?.Invoke();
            Hide();
        }

        public void FinishCall()
        {
            IsInCall = false;
            Hide();
        }

        public void Hide()
        {
            Initialize();
            IsRinging = false;
            IsInCall = false;
            SetState(PhoneCallState.Hidden);
            _followViewerPosition = false;
            SetMoveInteractionEnabled(false);
            KillTweens();

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
            IsInCall = false;
            SetState(PhoneCallState.Hidden);
            _followViewerPosition = false;
            KillTweens();

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            if (_content != null) _content.localScale = _baseScale;
            SetMoveInteractionEnabled(false);
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

            if (_panelGrab == null) _panelGrab = GetComponent<WorldSpacePanelGrab>();
            if (_moveCollider == null) _moveCollider = GetComponent<Collider>();
            _panelGrab?.Initialize();

            Rigidbody moveBody = GetComponent<Rigidbody>();
            if (moveBody != null)
            {
                moveBody.useGravity = false;
                moveBody.isKinematic = true;
            }

            IgnoreGameplayCollisions();

            _baseScale = _content != null ? _content.localScale : transform.localScale;

            if (_rejectButton != null) _rejectButton.onClick.AddListener(RejectCall);
            if (_acceptButton != null) _acceptButton.onClick.AddListener(AcceptCall);
            if (_hangUpButton != null) _hangUpButton.onClick.AddListener(HangUpCall);

            _isInitialized = true;
        }

        private void IgnoreGameplayCollisions()
        {
            if (_moveCollider == null) return;

            CharacterController[] playerControllers = FindObjectsByType<CharacterController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < playerControllers.Length; index++)
            {
                CharacterController controller = playerControllers[index];
                if (controller != null) Physics.IgnoreCollision(_moveCollider, controller, true);
            }

            PalletStacker[] vehicles = FindObjectsByType<PalletStacker>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < vehicles.Length; index++)
                IgnoreColliders(vehicles[index].GetComponentsInChildren<Collider>(true));

            PalletStackerLoad[] loads = FindObjectsByType<PalletStackerLoad>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < loads.Length; index++)
                IgnoreColliders(loads[index].GetComponentsInChildren<Collider>(true));
        }

        private void IgnoreColliders(Collider[] colliders)
        {
            if (_moveCollider == null || colliders == null) return;

            for (int index = 0; index < colliders.Length; index++)
            {
                Collider other = colliders[index];
                if (other != null && other != _moveCollider)
                    Physics.IgnoreCollision(_moveCollider, other, true);
            }
        }

        private void PlaceInFrontOfViewer()
        {
            if (!TryResolveViewer()) return;

            Quaternion yawRotation = Quaternion.Euler(0f, _viewer.eulerAngles.y, 0f);
            transform.position = _viewer.position + yawRotation * _viewerOffset;
            transform.rotation = yawRotation;
            _viewerOffset = _viewer.InverseTransformPoint(transform.position);
        }

        private bool TryResolveViewer()
        {
            if (_viewer == null && Camera.main != null) _viewer = Camera.main.transform;
            return _viewer != null;
        }

        private void SetRingingVisual()
        {
            SetLocalizedText(_statusLabel, LocalizationKeys.Phone.IncomingCall, "Incoming call");
            SetLocalizedText(_hintLabel, LocalizationKeys.Phone.TapToAnswer, "Tap to answer");
            if (_incomingActions != null) _incomingActions.SetActive(true);
            if (_activeCallActions != null) _activeCallActions.SetActive(false);
        }

        private void SetActiveCallVisual()
        {
            SetLocalizedText(_statusLabel, LocalizationKeys.Phone.InCall, "In call");
            if (_incomingActions != null) _incomingActions.SetActive(false);
            if (_activeCallActions != null) _activeCallActions.SetActive(true);
            UpdateCallDuration(0);
        }

        private void UpdateCallDuration(int elapsedSeconds)
        {
            if (_hintLabel == null) return;

            int hours = elapsedSeconds / 3600;
            int minutes = elapsedSeconds / 60 % 60;
            int seconds = elapsedSeconds % 60;
            _hintLabel.text = hours > 0
                ? $"{hours:00}:{minutes:00}:{seconds:00}"
                : $"{minutes:00}:{seconds:00}";
        }

        private void SetState(PhoneCallState nextState)
        {
            if (State == nextState) return;
            PhoneCallState previousState = State;
            State = nextState;
            StateChanged?.Invoke(previousState, nextState);
        }

        private void SetMoveInteractionEnabled(bool enabled)
        {
            _panelGrab?.SetEnabled(enabled);
        }

        private void KillTweens()
        {
            if (_canvasGroup != null) _canvasGroup.DOKill();
            if (_content != null) _content.DOKill();
        }

        private void SetLocalizedText(TMP_Text target, string key, string fallback)
        {
            if (target == null) return;
            target.text = _localization != null ? _localization.GetText(key) : fallback;
        }

        private void CacheLocalizedFonts()
        {
            if (_statusDefaultFont == null && _statusLabel != null)
                _statusDefaultFont = _statusLabel.font;
            if (_hintDefaultFont == null && _hintLabel != null)
                _hintDefaultFont = _hintLabel.font;
        }

        private void ApplyLocalizedFonts()
        {
            TMP_FontAsset fontOverride = _localization?.GetFontOverride();
            if (_statusLabel != null)
                _statusLabel.font = fontOverride != null ? fontOverride : _statusDefaultFont;
            if (_hintLabel != null)
                _hintLabel.font = fontOverride != null ? fontOverride : _hintDefaultFont;
        }
    }
}
