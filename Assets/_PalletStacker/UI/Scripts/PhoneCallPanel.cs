using System;
using DG.Tweening;
using ElectricPalletStackers.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    public enum PhoneCallState
    {
        Hidden,
        Ringing,
        InCall,
        Ended
    }

    /// <summary>
    /// Visual state for the phone call view. World placement is owned by
    /// <see cref="PhoneCallWorldPlacement"/> and event timing is owned by
    /// <see cref="PhoneCallEventSimulator"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class PhoneCallPanel : MonoBehaviour, ILocalizedView
    {
        [Header("View References")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _content;
        [SerializeField] private GameObject _incomingCallView;
        [SerializeField] private GameObject _ongoingCallView;
        [SerializeField] private GameObject _endedCallView;
        [SerializeField] private TMP_Text _ongoingTimerLabel;
        [SerializeField] private TMP_Text _statusLabel;
        [SerializeField] private TMP_Text _callerLabel;
        [SerializeField] private TMP_Text _hintLabel;
        [SerializeField] private GameObject _incomingActions;
        [SerializeField] private Button _acceptButton;
        [SerializeField] private Button _declineButton;
        [SerializeField] private Button _endButton;
        [SerializeField] private TMP_Text _endButtonLabel;

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float _transitionDuration = 0.2f;
        [SerializeField, Range(0.01f, 1f)] private float _ringingStartScale = 0.92f;
        [SerializeField, Range(0.01f, 1f)] private float _hiddenScale = 0.94f;
        [SerializeField] private bool _startHidden = true;

        private Vector3 _shownScale = Vector3.one;
        private bool _scaleCaptured;
        private bool _acceptButtonBound;
        private bool _endButtonBound;
        private bool _configurationErrorReported;
        private ILocalizationService _localization;
        private TMP_FontAsset _statusDefaultFont;
        private TMP_FontAsset _hintDefaultFont;
        private float _ongoingStartedAt;
        private int _displayedOngoingSecond = -1;

        public bool IsRinging { get; private set; }
        public bool IsInCall { get; private set; }
        public bool IsVisible => State != PhoneCallState.Hidden;
        public PhoneCallState State { get; private set; }
        public string CallerDisplay => _callerLabel != null ? _callerLabel.text : string.Empty;

        public event Action CallAccepted;
        public event Action CallEnded;
        public event Action<PhoneCallState, PhoneCallState> StateChanged;

        private void OnEnable()
        {
            CaptureAuthoredScale();
            BindAcceptButton();
            BindEndButton();
            if (_startHidden && State == PhoneCallState.Hidden) HideImmediate();
        }

        private void OnDisable()
        {
            UnbindAcceptButton();
            UnbindEndButton();
            KillTweens();
        }

        private void Update()
        {
            if (State != PhoneCallState.InCall || _ongoingTimerLabel == null) return;

            int elapsedSeconds = Mathf.Max(
                0,
                Mathf.FloorToInt(Time.unscaledTime - _ongoingStartedAt));
            UpdateOngoingTimer(elapsedSeconds);
        }

        public void ApplyLocalization(ILocalizationService localization)
        {
            _localization = localization;
            CacheLocalizedFonts();
            ApplyLocalizedFonts();

            if (State == PhoneCallState.InCall)
            {
                SetLocalizedText(_statusLabel, LocalizationKeys.Phone.InCall, "In call");
                SetLocalizedText(_hintLabel, LocalizationKeys.Phone.InCall, "In call");
            }
            else if (State == PhoneCallState.Ringing || State == PhoneCallState.Hidden)
            {
                SetLocalizedText(
                    _statusLabel,
                    LocalizationKeys.Phone.IncomingCall,
                    "Incoming call");
                SetLocalizedText(
                    _hintLabel,
                    LocalizationKeys.Phone.ChooseAction,
                    "Accept or decline");
                SetLocalizedText(_endButtonLabel, LocalizationKeys.Phone.Decline, "Decline");
            }

            SetLocalizedText(_callerLabel, LocalizationKeys.Phone.Boss, "Boss");
        }

        [ContextMenu("Simulate Incoming Call")]
        public void ShowDefaultIncomingCall()
        {
            ShowIncomingCall();
        }

        public void ShowIncomingCall()
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (!HasRequiredReferences()) return;

            _content.gameObject.SetActive(true);
            _canvasGroup.enabled = true;
            SetLocalizedText(_callerLabel, LocalizationKeys.Phone.Boss, "Boss");
            IsRinging = true;
            IsInCall = false;
            UpdateOngoingTimer(0, true);
            SetState(PhoneCallState.Ringing);
            SetRingingVisual();
            KillTweens();

            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
            _content.localScale = _shownScale * _ringingStartScale;

            _canvasGroup.DOFade(1f, _transitionDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(gameObject);
            _content.DOScale(_shownScale, _transitionDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        public void AcceptCall()
        {
            if (!IsRinging) return;

            IsRinging = false;
            IsInCall = true;
            _ongoingStartedAt = Time.unscaledTime;
            UpdateOngoingTimer(0, true);
            SetState(PhoneCallState.InCall);
            SetInCallVisual();
            CallAccepted?.Invoke();
        }

        public void ShowEnded()
        {
            if (!IsVisible || State == PhoneCallState.Ended) return;

            IsRinging = false;
            IsInCall = false;
            SetState(PhoneCallState.Ended);
            SetEndedVisual();
        }

        public void EndCall()
        {
            if (!IsRinging && !IsInCall) return;

            CallEnded?.Invoke();
        }

        public void Hide()
        {
            IsRinging = false;
            IsInCall = false;
            SetState(PhoneCallState.Hidden);
            SetPhaseVisible(null);
            KillTweens();

            if (!HasRequiredReferences()) return;

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.DOFade(0f, _transitionDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .SetLink(gameObject);
            _content.DOScale(_shownScale * _hiddenScale, _transitionDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        public void HideImmediate()
        {
            CaptureAuthoredScale();
            IsRinging = false;
            IsInCall = false;
            SetState(PhoneCallState.Hidden);
            SetPhaseVisible(null);
            KillTweens();

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            if (_content != null) _content.localScale = _shownScale;
        }

        private void BindAcceptButton()
        {
            if (_acceptButton == null || _acceptButtonBound) return;
            _acceptButton.onClick.AddListener(AcceptCall);
            _acceptButtonBound = true;
        }

        private void UnbindAcceptButton()
        {
            if (_acceptButton == null || !_acceptButtonBound) return;
            _acceptButton.onClick.RemoveListener(AcceptCall);
            _acceptButtonBound = false;
        }

        private void BindEndButton()
        {
            if (_endButtonBound) return;
            if (_declineButton != null) _declineButton.onClick.AddListener(EndCall);
            if (_endButton != null) _endButton.onClick.AddListener(EndCall);
            _endButtonBound = true;
        }

        private void UnbindEndButton()
        {
            if (!_endButtonBound) return;
            if (_declineButton != null) _declineButton.onClick.RemoveListener(EndCall);
            if (_endButton != null) _endButton.onClick.RemoveListener(EndCall);
            _endButtonBound = false;
        }

        private void CaptureAuthoredScale()
        {
            if (_scaleCaptured || _content == null) return;
            _shownScale = _content.localScale;
            _scaleCaptured = true;
        }

        private bool HasRequiredReferences()
        {
            if (_canvasGroup != null && _content != null) return true;

            if (!_configurationErrorReported)
            {
                Debug.LogError(
                    "PhoneCallPanel is not configured. Assign Canvas Group and Content " +
                    "on the Phone Call View in the Inspector.",
                    this);
                _configurationErrorReported = true;
            }

            return false;
        }

        private void SetRingingVisual()
        {
            SetPhaseVisible(_incomingCallView);
            SetLocalizedText(
                _statusLabel,
                LocalizationKeys.Phone.IncomingCall,
                "Incoming call");
            SetLocalizedText(
                _hintLabel,
                LocalizationKeys.Phone.ChooseAction,
                "Accept or decline");
            if (_incomingActions != null) _incomingActions.SetActive(true);
            if (_acceptButton != null) _acceptButton.gameObject.SetActive(true);
            if (_declineButton != null) _declineButton.gameObject.SetActive(true);
            SetLocalizedText(_endButtonLabel, LocalizationKeys.Phone.Decline, "Decline");
        }

        private void SetInCallVisual()
        {
            SetPhaseVisible(_ongoingCallView);
            SetLocalizedText(_statusLabel, LocalizationKeys.Phone.InCall, "In call");
            SetLocalizedText(_hintLabel, LocalizationKeys.Phone.InCall, "In call");
            if (_endButton != null) _endButton.gameObject.SetActive(true);
        }

        private void SetEndedVisual()
        {
            SetPhaseVisible(_endedCallView);
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        private void SetPhaseVisible(GameObject visibleView)
        {
            SetViewActive(_incomingCallView, _incomingCallView == visibleView);
            SetViewActive(_ongoingCallView, _ongoingCallView == visibleView);
            SetViewActive(_endedCallView, _endedCallView == visibleView);
        }

        private static void SetViewActive(GameObject view, bool active)
        {
            if (view != null && view.activeSelf != active) view.SetActive(active);
        }

        private void UpdateOngoingTimer(int elapsedSeconds, bool force = false)
        {
            if (_ongoingTimerLabel == null ||
                (!force && elapsedSeconds == _displayedOngoingSecond)) return;

            _displayedOngoingSecond = elapsedSeconds;
            int minutes = elapsedSeconds / 60;
            int seconds = elapsedSeconds % 60;
            _ongoingTimerLabel.text = $"{minutes:00}:{seconds:00}";
        }

        private void SetState(PhoneCallState nextState)
        {
            if (State == nextState) return;
            PhoneCallState previousState = State;
            State = nextState;
            StateChanged?.Invoke(previousState, nextState);
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
                _statusLabel.font = fontOverride != null
                    ? fontOverride
                    : _statusDefaultFont;
            if (_hintLabel != null)
                _hintLabel.font = fontOverride != null
                    ? fontOverride
                    : _hintDefaultFont;
        }
    }
}
