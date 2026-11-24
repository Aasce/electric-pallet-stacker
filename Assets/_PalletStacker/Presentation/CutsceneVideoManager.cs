using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ElectricPalletStackers.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CutsceneVideoManager : MonoBehaviour
    {
        private static CutsceneVideoManager _instance;

        private readonly Dictionary<string, CutsceneVideoSource> _sources =
            new Dictionary<string, CutsceneVideoSource>();

        public static CutsceneVideoSource RegisterSource(
            string displayName,
            string fileName,
            RawImage display)
        {
            if (display == null)
            {
                Debug.LogError($"Cannot register cutscene '{fileName}': display RawImage is missing.");
                return null;
            }

            CutsceneVideoManager manager = GetOrCreate();
            if (manager._sources.TryGetValue(fileName, out CutsceneVideoSource existing) &&
                existing != null)
            {
                if (existing.TargetTexture != null)
                    display.texture = existing.TargetTexture;
                existing.Prepare();
                return existing;
            }

            if (!(display.texture is RenderTexture displayTexture))
            {
                Debug.LogError($"Cannot register cutscene '{fileName}': display texture is not a RenderTexture.", display);
                return null;
            }

            RenderTexture outputTexture = new RenderTexture(displayTexture.descriptor)
            {
                name = $"{displayName} Cutscene Output",
                filterMode = displayTexture.filterMode,
                wrapMode = displayTexture.wrapMode
            };
            outputTexture.Create();
            display.texture = outputTexture;

            GameObject playerObject = new GameObject($"{displayName} Video Player");
            playerObject.transform.SetParent(manager.transform, false);

            VideoPlayer player = playerObject.AddComponent<VideoPlayer>();
            player.source = VideoSource.Url;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = outputTexture;
            player.aspectRatio = VideoAspectRatio.FitInside;
            player.timeUpdateMode = VideoTimeUpdateMode.DSPTime;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.playOnAwake = false;
            player.skipOnDrop = true;
            player.waitForFirstFrame = true;

            AudioSource audioSource = playerObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            CutsceneVideoSource source = playerObject.AddComponent<CutsceneVideoSource>();
            source.ConfigureAndPrepare(fileName);
            manager._sources.Add(fileName, source);
            return source;
        }

        private static CutsceneVideoManager GetOrCreate()
        {
            if (_instance != null) return _instance;

            _instance = FindFirstObjectByType<CutsceneVideoManager>();
            if (_instance != null) return _instance;

            GameObject managerObject = new GameObject(nameof(CutsceneVideoManager));
            return managerObject.AddComponent<CutsceneVideoManager>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;

            foreach (CutsceneVideoSource source in _sources.Values)
            {
                RenderTexture targetTexture = source != null ? source.TargetTexture : null;
                if (targetTexture == null) continue;
                targetTexture.Release();
                Destroy(targetTexture);
            }
        }
    }
}
