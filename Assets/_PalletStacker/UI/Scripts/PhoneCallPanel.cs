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
        InCall
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
        [SerializeField] private TMP_Text _statusLabel;
        [SerializeField] private TMP_Text _callerLabel;
        [SerializeField] private TMP_Text _hintLabel;
        [SerializeField] private GameObject _incomingActions;
        [SerializeField] private GameObject _activeCallActions;
        [SerializeField] private Button _acceptButton;

        [Header("Presentation")]
        [SerializeField] private string _defaultCaller = "0123456789";
        [SerializeField, Min(0f)] private float _transitionDuration = 0.2f;
        [SerializeField, Range(0.01f, 1f)] private float _ringingStartScale = 0.92f;
        [SerializeField, Range(0.01f, 1f)] private float _hiddenScale = 0.94f;
        [SerializeField] private bool _startHidden = true;

        private Vector3 _shownScale = Vector3.one;
        private bool _scaleCaptured;
        private bool _acceptButtonBound;
        private bool _configurationErrorReported;
        private float _callStartedAt;
        private int _lastDisplayedCallSecond = -1;
        private ILocalizationService _localization;
        private TMP_FontAsset _statusDefaultFont;
        private TMP_FontAsset _hintDefaultFont;

        public bool IsRinging { get; private set; }
        public bool IsInCall { get; private set; }
        public PhoneCallState State { get; private set; }
        public string CallerDisplay => _callerLabel != null ? _callerLabel.text : string.Empty;

        public event Action CallAccepted;
        public event Action<PhoneCallState, PhoneCallState> StateChanged;

        private void OnEnable()
        {
            CaptureAuthoredScale();
            BindAcceptButton();
            if (_startHidden && State == PhoneCallState.Hidden) HideImmediate();
        }

        private void OnDisable()
        {
            UnbindAcceptButton();
            KillTweens();
        }

        private void Update()
        {
            if (!IsInCall) return;

            int elapsedSeconds = Mathf.Max(
                0,
                Mathf.FloorToInt(Time.realtimeSinceStartup - _callStartedAt));
            if (elapsedSeconds == _lastDisplayedCallSecond) return;

            _lastDisplayedCallSecond = elapsedSeconds;
            UpdateCallDuration(elapsedSeconds);
        }

        public void ApplyLocalization(ILocalizationService localization)
        {
            _localization = localization;
            CacheLocalizedFonts();
            ApplyLocalizedFonts();

            if (IsInCall)
            {
                SetLocalizedText(_statusLabel, LocalizationKeys.Phone.InCall, "In call");
                if (_lastDisplayedCallSecond >= 0)
                    UpdateCallDuration(_lastDisplayedCallSecond);
                return;
            }

            SetLocalizedText(
                _statusLabel,
                LocalizationKeys.Phone.IncomingCall,
                "Incoming call");
            SetLocalizedText(
                _hintLabel,
                LocalizationKeys.Phone.TapToAnswer,
                "Tap to answer");
        }

        [ContextMenu("Simulate Incoming Call")]
        public void ShowDefaultIncomingCall()
        {
            ShowIncomingCall(_defaultCaller);
        }

        public void ShowIncomingCall(string callerDisplay)
        {
            if (!HasRequiredReferences()) return;

            SetCallerDisplay(string.IsNullOrWhiteSpace(callerDisplay)
                ? _defaultCaller
                : callerDisplay);
            IsRinging = true;
            IsInCall = false;
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

        public void FinishCall()
        {
            IsInCall = false;
            Hide();
        }

        public void Hide()
        {
            IsRinging = false;
            IsInCall = false;
            SetState(PhoneCallState.Hidden);
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
            SetLocalizedText(
                _statusLabel,
                LocalizationKeys.Phone.IncomingCall,
                "Incoming call");
            SetLocalizedText(
                _hintLabel,
                LocalizationKeys.Phone.TapToAnswer,
                "Tap to answer");
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
