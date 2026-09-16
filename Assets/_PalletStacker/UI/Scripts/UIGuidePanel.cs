using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class UIGuidePanel : UIPanel
    {
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private UIInputRouter _inputRouter;
        [SerializeField, Min(0f)] private float _scrollSpeed = 0.65f;

        private void Update()
        {
            if (_scrollRect == null || _inputRouter == null) return;

            float position = _scrollRect.verticalNormalizedPosition +
                             _inputRouter.LatestTravelAxis * _scrollSpeed * Time.unscaledDeltaTime;
            _scrollRect.verticalNormalizedPosition = Mathf.Clamp01(position);
        }

        public override void ResetView()
        {
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
        }
    }
}
