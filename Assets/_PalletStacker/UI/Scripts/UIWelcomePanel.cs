using System;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class UIWelcomePanel : UIPanel
    {
        [SerializeField] private Button _continueButton;

        public event Action ContinueRequested;

        protected override void Awake()
        {
            base.Awake();
            if (_continueButton != null)
                _continueButton.onClick.AddListener(HandleContinueClicked);
        }

        private void OnDestroy()
        {
            if (_continueButton != null)
                _continueButton.onClick.RemoveListener(HandleContinueClicked);
        }

        private void HandleContinueClicked() => ContinueRequested?.Invoke();
    }
}
