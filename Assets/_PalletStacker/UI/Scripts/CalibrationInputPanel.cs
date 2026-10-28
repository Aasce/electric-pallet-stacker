using System;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    /// <summary>Thin view/input adapter. It emits commands and owns no calibration behavior.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldSpacePanelGrab))]
    public sealed class CalibrationInputPanel : UIPanel
    {
        [SerializeField] private WorldSpacePanelGrab _panelGrab;
        [SerializeField] private Button _upButton;
        [SerializeField] private Button _downButton;
        [SerializeField] private Button _leftButton;
        [SerializeField] private Button _centerButton;
        [SerializeField] private Button _rightButton;
        [SerializeField] private Button _forwardButton;
        [SerializeField] private Button _backButton;
        [SerializeField] private Button _continueButton;
        private bool _buttonsBound;
        public event Action AlignRequested;
        public event Action<Vector3> OffsetAdjustmentRequested;
        public event Action ContinueRequested;

        protected override void Awake()
        {
            base.Awake();
        }

        public override void Show()
        {
            BindButtons();
            base.Show();
            _panelGrab?.SetEnabled(true);
        }

        public override void Hide()
        {
            _panelGrab?.SetEnabled(false);
            base.Hide();
        }

        public void PressCenter() => AlignRequested?.Invoke();
        public void PressUp() => OffsetAdjustmentRequested?.Invoke(Vector3.up);
        public void PressDown() => OffsetAdjustmentRequested?.Invoke(Vector3.down);
        public void PressLeft() => OffsetAdjustmentRequested?.Invoke(Vector3.left);
        public void PressRight() => OffsetAdjustmentRequested?.Invoke(Vector3.right);
        public void PressForward() => OffsetAdjustmentRequested?.Invoke(Vector3.forward);
        public void PressBack() => OffsetAdjustmentRequested?.Invoke(Vector3.back);
        public void PressContinue() => ContinueRequested?.Invoke();

        private void BindButtons()
        {
            if (_buttonsBound) return;
            BindButton(_upButton, PressUp);
            BindButton(_downButton, PressDown);
            BindButton(_leftButton, PressLeft);
            BindButton(_centerButton, PressCenter);
            BindButton(_rightButton, PressRight);
            BindButton(_forwardButton, PressForward);
            BindButton(_backButton, PressBack);
            BindButton(_continueButton, PressContinue);
            _buttonsBound = true;
        }

        private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }
    }
}
