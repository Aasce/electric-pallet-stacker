using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace ElectricPalletStackers.Presentation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class CutsceneVideoSource : MonoBehaviour
    {
        private const string CutsceneFolderName = "Cutscenes";

        private VideoPlayer _videoPlayer;
        private string _fileName;

        private bool _initialized;
        private bool _loading;
        private bool _prepared;
        private bool _failed;
        private bool _playWhenPrepared;

        public bool IsPrepared => _prepared && _videoPlayer != null && _videoPlayer.isPrepared;
        public bool HasFailed => _failed;
        public VideoPlayer Player
        {
            get
            {
                Initialize();
                return _videoPlayer;
            }
        }
        public RenderTexture TargetTexture => Player != null ? Player.targetTexture : null;

        private void OnDestroy()
        {
            if (_videoPlayer == null) return;
            _videoPlayer.prepareCompleted -= HandlePrepared;
            _videoPlayer.errorReceived -= HandleVideoError;
        }

        private void OnDisable()
        {
            _loading = false;
            _prepared = false;
            _playWhenPrepared = false;
        }

        public void Prepare()
        {
            Initialize();
            if (_loading || IsPrepared) return;
            _prepared = false;
            if (string.IsNullOrWhiteSpace(_fileName))
            {
                Fail("Cutscene video file name is empty.");
                return;
            }
            _failed = false;
            _loading = true;
            StartCoroutine(ResolveAndPrepareVideo());
        }

        public void ConfigureAndPrepare(string fileName)
        {
            _fileName = fileName;
            Prepare();
        }

        public void Play()
        {
            Prepare();
            if (IsPrepared)
            {
                _videoPlayer.time = 0d;
                _videoPlayer.Play();
                return;
            }

            _playWhenPrepared = true;
        }

        public void Stop()
        {
            _playWhenPrepared = false;
            if (_videoPlayer != null && _videoPlayer.isPlaying)
                _videoPlayer.Stop();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            if (_videoPlayer == null) _videoPlayer = GetComponent<VideoPlayer>();
            if (_videoPlayer == null) return;

            _videoPlayer.prepareCompleted += HandlePrepared;
            _videoPlayer.errorReceived += HandleVideoError;
            _videoPlayer.Stop();
            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.clip = null;
        }

        private IEnumerator ResolveAndPrepareVideo()
        {
            // Start preparation after scene activation, outside the initial Awake work.
            yield return null;

            string overridePath = Path.Combine(
                Application.persistentDataPath,
                CutsceneFolderName,
                _fileName);

            string streamingAssetsPath = Path.Combine(
                Application.streamingAssetsPath,
                CutsceneFolderName,
                _fileName);
            string videoPath = File.Exists(overridePath)
                ? overridePath
                : streamingAssetsPath;

            if (!File.Exists(videoPath))
            {
                Fail($"Cutscene file was not found: {videoPath}");
                yield break;
            }

            try
            {
                PrepareFromFile(videoPath);
            }
            catch (Exception exception)
            {
                Fail($"Could not prepare cutscene video '{_fileName}': {exception.Message}");
            }
        }

        private void PrepareFromFile(string path)
        {
            _videoPlayer.url = new Uri(Path.GetFullPath(path)).AbsoluteUri;
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
                _videoPlayer.controlledAudioTrackCount = 1;
                _videoPlayer.EnableAudioTrack(0, true);
                _videoPlayer.SetTargetAudioSource(0, audioSource);
            }
            else
            {
                Debug.LogWarning($"Cutscene video '{_fileName}' has no AudioSource; it will play without audio.", this);
            }
            _videoPlayer.Prepare();
        }

        private void HandlePrepared(VideoPlayer source)
        {
            if (source != _videoPlayer) return;
            _loading = false;
            _prepared = true;
            _failed = false;

            if (!_playWhenPrepared) return;
            _playWhenPrepared = false;
            _videoPlayer.time = 0d;
            _videoPlayer.Play();
        }

        private void HandleVideoError(VideoPlayer source, string message)
        {
            if (source != _videoPlayer) return;
            _loading = false;
            _failed = true;
            _playWhenPrepared = false;
            Debug.LogWarning($"Cutscene video '{_fileName}' failed: {message}", this);
        }

        private void Fail(string message)
        {
            _loading = false;
            _failed = true;
            _playWhenPrepared = false;
            Debug.LogWarning(message, this);
        }
    }
}
