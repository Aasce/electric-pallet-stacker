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
        private bool _buttonsBound;
        public event Action AlignRequested;
        public event Action<Vector2> OffsetAdjustmentRequested;

        protected override void Awake()
        {
            base.Awake();
            if (_panelGrab == null) _panelGrab = GetComponent<WorldSpacePanelGrab>();
            _panelGrab?.Initialize();
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
        public void PressUp() => OffsetAdjustmentRequested?.Invoke(Vector2.up);
        public void PressDown() => OffsetAdjustmentRequested?.Invoke(Vector2.down);
        public void PressLeft() => OffsetAdjustmentRequested?.Invoke(Vector2.left);
        public void PressRight() => OffsetAdjustmentRequested?.Invoke(Vector2.right);

        private void BindButtons()
        {
            if (_buttonsBound) return;
            BindButton("Up", PressUp);
            BindButton("Down", PressDown);
            BindButton("Left", PressLeft);
            BindButton("Center", PressCenter);
            BindButton("Right", PressRight);
            _buttonsBound = true;
        }

        private void BindButton(string buttonName, UnityEngine.Events.UnityAction action)
        {
            Transform buttonTransform = transform.Find("Calibration Canvas/" + buttonName);
            Button button = buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
            if (button != null) button.onClick.AddListener(action);
        }
    }
}
