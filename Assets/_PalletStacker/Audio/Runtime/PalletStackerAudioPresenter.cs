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
        private AudioHandle _hornLoop;
        private AudioHandle _engineStartup;
        private AudioHandle _engineLoop;
        private Coroutine _hornRoutine;
        private Coroutine _engineRoutine;
        private bool _hornActive;

        private void Awake()
        {
            if (_hornOutput == null) _hornOutput = GetComponentInChildren<PalletStackerHornOutput>(true);
            if (_motor == null) _motor = GetComponent<PalletStackerRigidbodyMotor>();
            if (_followTarget == null) _followTarget = transform;
        }

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
            _hornLoop.Stop();
            _engineStartup.Stop();
            _engineLoop.Stop();
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
            _engineLoop.Stop();

            if (moving) _engineRoutine = StartCoroutine(PlayEngineSequence());
        }

        private void HandleHornChanged(bool active)
        {
            _hornActive = active;
            if (_hornRoutine != null) StopCoroutine(_hornRoutine);
            _hornStart.Stop();
            _hornLoop.Stop();

            if (active)
                _hornRoutine = StartCoroutine(PlayHornSequence());
            else
                AudioManager.Instance?.Play(PalletStackerAudioKeys.HornEnd, _followTarget);
        }

        private IEnumerator PlayHornSequence()
        {
            AudioManager manager = AudioManager.Instance;
            if (manager == null) yield break;

            _hornStart = manager.Play(PalletStackerAudioKeys.HornStart, _followTarget);
            while (_hornActive && _hornStart.IsPlaying) yield return null;

            if (_hornActive)
            {
                _hornLoop = manager.Play(
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
            while (_engineStartup.IsPlaying) yield return null;

            if (isActiveAndEnabled && _motor != null && Mathf.Abs(_motor.CurrentSpeed) > 0.0001f)
            {
                _engineLoop = manager.Play(
                    PalletStackerAudioKeys.VehicleLoop,
                    _followTarget,
                    new AudioPlayOptions { Loop = true });
            }

            _engineRoutine = null;
        }
    }
}
