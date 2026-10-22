using System.Collections;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Audio
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerAudioPresenter : MonoBehaviour
    {
        [SerializeField] private PalletStackerHornOutput _hornOutput;
        [SerializeField] private PalletStackerRigidbodyMotor _motor;
        [SerializeField] private Transform _followTarget;

        private AudioHandle _hornStart;
        private AudioHandle _hornStartBoost;
        private AudioHandle _hornLoop;
        private AudioHandle _hornLoopBoost;
        private AudioHandle _engineStartup;
        private AudioHandle _engineStartupBoost;
        private AudioHandle _engineLoop;
        private AudioHandle _engineLoopBoost;
        private Coroutine _hornRoutine;
        private Coroutine _engineRoutine;
        private bool _hornActive;

        private void OnEnable()
        {
            if (_hornOutput != null) _hornOutput.HornChanged += HandleHornChanged;
            if (_motor != null) _motor.MovementChanged += HandleMovementChanged;
        }

        private void OnDisable()
        {
            if (_hornOutput != null) _hornOutput.HornChanged -= HandleHornChanged;
            if (_motor != null) _motor.MovementChanged -= HandleMovementChanged;
            StopAllCoroutines();
            _hornRoutine = null;
            _engineRoutine = null;
            _hornStart.Stop();
            _hornStartBoost.Stop();
            _hornLoop.Stop();
            _hornLoopBoost.Stop();
            _engineStartup.Stop();
            _engineStartupBoost.Stop();
            _engineLoop.Stop();
            _engineLoopBoost.Stop();
            _hornActive = false;
        }

        private void HandleMovementChanged(bool moving)
        {
            if (_engineRoutine != null)
            {
                StopCoroutine(_engineRoutine);
                _engineRoutine = null;
            }

            _engineStartup.Stop();
            _engineStartupBoost.Stop();
            _engineLoop.Stop();
            _engineLoopBoost.Stop();

            if (moving) _engineRoutine = StartCoroutine(PlayEngineSequence());
        }

        private void HandleHornChanged(bool active)
        {
            _hornActive = active;
            if (_hornRoutine != null) StopCoroutine(_hornRoutine);
            _hornStart.Stop();
            _hornStartBoost.Stop();
            _hornLoop.Stop();
            _hornLoopBoost.Stop();

            if (active)
                _hornRoutine = StartCoroutine(PlayHornSequence());
            else
                PlayHornEndDoubled();
        }

        private IEnumerator PlayHornSequence()
        {
            AudioManager manager = AudioManager.Instance;
            if (manager == null) yield break;

            // AudioSource.volume is capped at 1. Layering the same horn voice twice
            // produces the requested 2x linear amplitude without rewriting source clips.
            _hornStart = manager.Play(PalletStackerAudioKeys.HornStart, _followTarget);
            _hornStartBoost = manager.Play(PalletStackerAudioKeys.HornStart, _followTarget);
            while (_hornActive && (_hornStart.IsPlaying || _hornStartBoost.IsPlaying))
                yield return null;

            if (_hornActive)
            {
                _hornLoop = manager.Play(
                    PalletStackerAudioKeys.HornLoop,
                    _followTarget,
                    new AudioPlayOptions { Loop = true });
                _hornLoopBoost = manager.Play(
                    PalletStackerAudioKeys.HornLoop,
                    _followTarget,
                    new AudioPlayOptions { Loop = true });
            }

            _hornRoutine = null;
        }

        private IEnumerator PlayEngineSequence()
        {
            AudioManager manager = AudioManager.Instance;
            if (manager == null) yield break;

            _engineStartup = manager.Play(PalletStackerAudioKeys.VehicleStartup, _followTarget);
            _engineStartupBoost = manager.Play(
                PalletStackerAudioKeys.VehicleStartup,
                _followTarget,
                new AudioPlayOptions { Volume = 0.2f });
            while (_engineStartup.IsPlaying || _engineStartupBoost.IsPlaying) yield return null;

            if (isActiveAndEnabled && _motor != null && Mathf.Abs(_motor.CurrentSpeed) > 0.0001f)
            {
                _engineLoop = manager.Play(
                    PalletStackerAudioKeys.VehicleLoop,
                    _followTarget,
                    new AudioPlayOptions { Loop = true });
                _engineLoopBoost = manager.Play(
                    PalletStackerAudioKeys.VehicleLoop,
                    _followTarget,
                    new AudioPlayOptions { Loop = true, Volume = 0.2f });
            }

            _engineRoutine = null;
        }

        private void PlayHornEndDoubled()
        {
            AudioManager manager = AudioManager.Instance;
            if (manager == null) return;

            manager.Play(PalletStackerAudioKeys.HornEnd, _followTarget);
            manager.Play(PalletStackerAudioKeys.HornEnd, _followTarget);
        }
    }
}
