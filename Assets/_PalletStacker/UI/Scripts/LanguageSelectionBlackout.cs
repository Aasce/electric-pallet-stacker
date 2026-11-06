using System.Collections.Generic;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    /// <summary>
    /// Hides the surrounding world while keeping the language UI and tracked-player
    /// visuals available. Every changed render state is restored after selection.
    /// </summary>
    internal sealed class LanguageSelectionBlackout
    {
        private readonly List<RendererState> _rendererStates = new();
        private readonly List<Canvas> _disabledCanvases = new();
        private readonly List<Terrain> _disabledTerrains = new();
        private readonly List<CameraState> _cameraStates = new();

        public bool IsActive { get; private set; }

        public void ShowOnly(Transform visibleUiRoot)
        {
            if (IsActive || visibleUiRoot == null) return;

            IsActive = true;
            Transform playerRoot = Camera.main != null ? Camera.main.transform.root : null;

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null ||
                    IsRelatedTo(renderer.transform, visibleUiRoot) ||
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
                    IsRelatedTo(canvas.transform, visibleUiRoot)) continue;

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

                _cameraStates.Add(new CameraState(
                    camera,
                    camera.clearFlags,
                    camera.backgroundColor));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }
        }

        public void Restore()
        {
            if (!IsActive) return;

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
            IsActive = false;
        }

        private static bool IsRelatedTo(Transform candidate, Transform root)
        {
            if (candidate == null || root == null) return false;
            return candidate == root ||
                   candidate.IsChildOf(root) ||
                   root.IsChildOf(candidate);
        }

        private static bool IsUnder(Transform candidate, Transform root)
        {
            if (candidate == null || root == null) return false;
            return candidate == root || candidate.IsChildOf(root);
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
            public CameraState(
                Camera camera,
                CameraClearFlags clearFlags,
                Color backgroundColor)
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
