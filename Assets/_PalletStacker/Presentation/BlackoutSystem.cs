using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricPalletStackers.Presentation
{
    /// <summary>
    /// Owns headset-camera fades and temporary world visibility changes.
    /// The fade is rendered by the target VR camera, not the desktop manager canvas.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class BlackoutSystem : MonoBehaviour
    {
        public static BlackoutSystem Instance { get; private set; }

        [Header("Camera Fade")]
        [SerializeField] private Canvas _cameraFadeCanvas;
        [SerializeField] private CanvasGroup _cameraFadeGroup;
        [SerializeField] private Camera _targetCamera;
        [SerializeField, Min(0f)] private float _fadeToBlackDuration = 1f;
        [SerializeField, Min(0f)] private float _fadeFromBlackDuration = 1f;

        private readonly List<RendererState> _rendererStates = new();
        private readonly List<Canvas> _disabledCanvases = new();
        private readonly List<Terrain> _disabledTerrains = new();
        private readonly List<CameraState> _cameraStates = new();

        private bool _isHidingWorld;
        private bool _isFading;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("Only one BlackoutSystem may be active in a scene.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveVisualReferences();
            ConfigureCamera();

            if (_cameraFadeGroup != null)
            {
                _cameraFadeGroup.alpha = 0f;
                _cameraFadeGroup.interactable = false;
                _cameraFadeGroup.blocksRaycasts = false;
            }

            if (_cameraFadeCanvas != null)
                _cameraFadeCanvas.enabled = false;
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        private void OnDisable()
        {
            RestoreWorld();
            ResetFade();
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public IEnumerator FadeToBlack()
        {
            return FadeToBlack(_fadeToBlackDuration);
        }

        public IEnumerator FadeToBlack(float duration)
        {
            return FadeTo(1f, duration);
        }

        public IEnumerator FadeFromBlack()
        {
            return FadeFromBlack(_fadeFromBlackDuration);
        }

        public IEnumerator FadeFromBlack(float duration)
        {
            return FadeTo(0f, duration);
        }

        /// <summary>
        /// Hides the scene while leaving a requested world UI subtree visible.
        /// ScreenSpaceOverlay canvases, such as desktop manager controls, are untouched.
        /// </summary>
        public void SetWorldVisibleOnly(Transform visibleWorldRoot)
        {
            ApplyWorldIsolation(visibleWorldRoot);
        }

        /// <summary>Hides the rendered world while a reset is performed under full black.</summary>
        public void HideWorld()
        {
            ApplyWorldIsolation(null);
        }

        /// <summary>Restores every renderer, canvas, terrain, and camera changed by isolation.</summary>
        public void RestoreWorld()
        {
            if (!_isHidingWorld) return;

            for (int index = 0; index < _rendererStates.Count; index++)
            {
                RendererState state = _rendererStates[index];
                if (state.Renderer != null)
                    state.Renderer.forceRenderingOff = state.ForceRenderingOff;
            }

            for (int index = 0; index < _disabledCanvases.Count; index++)
            {
                Canvas canvas = _disabledCanvases[index];
                if (canvas != null) canvas.enabled = true;
            }

            for (int index = 0; index < _disabledTerrains.Count; index++)
            {
                Terrain terrain = _disabledTerrains[index];
                if (terrain != null) terrain.enabled = true;
            }

            for (int index = 0; index < _cameraStates.Count; index++)
            {
                CameraState state = _cameraStates[index];
                if (state.Camera == null) continue;
                state.Camera.clearFlags = state.ClearFlags;
                state.Camera.backgroundColor = state.BackgroundColor;
            }

            _rendererStates.Clear();
            _disabledCanvases.Clear();
            _disabledTerrains.Clear();
            _cameraStates.Clear();
            _isHidingWorld = false;
        }

        /// <summary>Immediately makes the fade transparent and stops waiting callers.</summary>
        public void ResetFade()
        {
            _isFading = false;
            if (_cameraFadeGroup != null) _cameraFadeGroup.alpha = 0f;
            if (_cameraFadeCanvas != null) _cameraFadeCanvas.enabled = false;
        }

        private IEnumerator FadeTo(float targetAlpha, float duration)
        {
            ResolveVisualReferences();
            if (_cameraFadeGroup == null)
            {
                Debug.LogError("BlackoutSystem requires a CanvasGroup on its camera fade prefab.", this);
                yield break;
            }

            while (_isFading) yield return null;

            _isFading = true;
            try
            {
                ConfigureCamera();
                if (_cameraFadeCanvas != null) _cameraFadeCanvas.enabled = true;

                float startAlpha = _cameraFadeGroup.alpha;
                float fadeDuration = Mathf.Max(0f, duration);
                if (fadeDuration <= 0f || Mathf.Approximately(startAlpha, targetAlpha))
                {
                    _cameraFadeGroup.alpha = targetAlpha;
                }
                else
                {
                    float elapsed = 0f;
                    while (elapsed < fadeDuration)
                    {
                        elapsed += Time.unscaledDeltaTime;
                        _cameraFadeGroup.alpha = Mathf.Lerp(
                            startAlpha,
                            targetAlpha,
                            Mathf.Clamp01(elapsed / fadeDuration));
                        yield return null;
                    }

                    _cameraFadeGroup.alpha = targetAlpha;
                }

                if (targetAlpha <= 0f && _cameraFadeCanvas != null)
                    _cameraFadeCanvas.enabled = false;
            }
            finally
            {
                _isFading = false;
            }
        }

        private void ApplyWorldIsolation(Transform visibleWorldRoot)
        {
            if (_isHidingWorld) return;

            _isHidingWorld = true;
            ConfigureCamera();
            Transform playerRoot = _targetCamera != null ? _targetCamera.transform.root : null;

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null ||
                    IsRelatedTo(renderer.transform, visibleWorldRoot) ||
                    IsUnder(renderer.transform, playerRoot)) continue;

                _rendererStates.Add(new RendererState(renderer, renderer.forceRenderingOff));
                renderer.forceRenderingOff = true;
            }

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < canvases.Length; index++)
            {
                Canvas canvas = canvases[index];
                if (canvas == null || !canvas.enabled ||
                    canvas.renderMode == RenderMode.ScreenSpaceOverlay ||
                    IsRelatedTo(canvas.transform, visibleWorldRoot) ||
                    IsRelatedTo(canvas.transform, transform)) continue;

                _disabledCanvases.Add(canvas);
                canvas.enabled = false;
            }

            Terrain[] terrains = Object.FindObjectsByType<Terrain>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < terrains.Length; index++)
            {
                Terrain terrain = terrains[index];
                if (terrain == null || !terrain.enabled ||
                    IsUnder(terrain.transform, playerRoot)) continue;

                _disabledTerrains.Add(terrain);
                terrain.enabled = false;
            }

            Camera[] cameras = Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < cameras.Length; index++)
            {
                Camera camera = cameras[index];
                if (camera == null || !camera.enabled) continue;

                _cameraStates.Add(new CameraState(camera, camera.clearFlags, camera.backgroundColor));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }
        }

        private void ResolveVisualReferences()
        {
            if (_cameraFadeCanvas == null)
                _cameraFadeCanvas = GetComponentInChildren<Canvas>(true);
            if (_cameraFadeGroup == null && _cameraFadeCanvas != null)
                _cameraFadeGroup = _cameraFadeCanvas.GetComponent<CanvasGroup>();
        }

        private void ConfigureCamera()
        {
            ResolveVisualReferences();
            if (_targetCamera == null) _targetCamera = Camera.main;
            if (_cameraFadeCanvas == null) return;

            _cameraFadeCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _cameraFadeCanvas.worldCamera = _targetCamera;
            _cameraFadeCanvas.overrideSorting = true;
            _cameraFadeCanvas.sortingOrder = short.MaxValue;
            _cameraFadeCanvas.planeDistance = _targetCamera != null
                ? Mathf.Max(0.1f, _targetCamera.nearClipPlane + 0.01f)
                : 0.1f;
        }

        private static bool IsRelatedTo(Transform candidate, Transform root)
        {
            if (candidate == null || root == null) return false;
            return candidate == root || candidate.IsChildOf(root) || root.IsChildOf(candidate);
        }

        private static bool IsUnder(Transform candidate, Transform root)
        {
            return candidate != null && root != null &&
                   (candidate == root || candidate.IsChildOf(root));
        }

        private readonly struct RendererState
        {
            public RendererState(Renderer renderer, bool forceRenderingOff)
            {
                Renderer = renderer;
                ForceRenderingOff = forceRenderingOff;
            }

            public Renderer Renderer { get; }
            public bool ForceRenderingOff { get; }
        }

        private readonly struct CameraState
        {
            public CameraState(Camera camera, CameraClearFlags clearFlags, Color backgroundColor)
            {
                Camera = camera;
                ClearFlags = clearFlags;
                BackgroundColor = backgroundColor;
            }

            public Camera Camera { get; }
            public CameraClearFlags ClearFlags { get; }
            public Color BackgroundColor { get; }
        }
    }
}
