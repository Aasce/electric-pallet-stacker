using System;
using System.Collections;
using System.Collections.Generic;
using ElectricPalletStackers.Audio;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class AppManager : MonoBehaviour
    {
        [Header("Composition")]
        [Tooltip("Components implementing IGameRoundParticipant.")]
        [SerializeField] private MonoBehaviour[] _roundParticipantComponents;
        [Tooltip("Components implementing IGameVictorySource.")]
        [SerializeField] private MonoBehaviour[] _victorySourceComponents;
        [Tooltip("Components implementing IGameFailureSource.")]
        [SerializeField] private MonoBehaviour[] _failureSourceComponents;
        [Tooltip("Components implementing IGameResettable.")]
        [SerializeField] private MonoBehaviour[] _resettableComponents;

        [Header("Startup")]
        [Tooltip("Leave disabled when UIFlowGameStarter starts the round after the guide UI closes.")]
        [SerializeField] private bool _autoStart;

        [Header("Result Audio")]
        [SerializeField, Min(0f)] private float _humanScreamDelay = 0.15f;

        private readonly List<IGameRoundParticipant> _roundParticipants = new();
        private readonly List<IGameRoundResumable> _roundResumables = new();
        private readonly List<IGameVictorySource> _victorySources = new();
        private readonly List<IGameFailureSource> _failureSources = new();
        private readonly List<IGameResettable> _resettables = new();
        private bool _sourcesSubscribed;
        private Coroutine _humanScreamRoutine;

        public GameState CurrentState { get; private set; } = GameState.Initializing;
        public GameFailureReason LastFailureReason { get; private set; } = GameFailureReason.Unknown;

        public event Action<GameState, GameState> StateChanged;
        public event Action RoundStarted;
        public event Action GameWon;
        public event Action<GameFailureReason> GameLost;

        private void Awake()
        {
            CacheComposition();
            ResetConfiguredState();
            TransitionTo(GameState.WaitingToStart);
        }

        private void OnEnable()
        {
            SubscribeSources();
        }

        private void Start()
        {
            if (_autoStart) StartRound();
        }

        private void OnDisable()
        {
            UnsubscribeSources();
            CancelPendingHumanScream();
        }

        public void StartRound()
        {
            if (CurrentState == GameState.Playing) return;

            CancelPendingHumanScream();
            LastFailureReason = GameFailureReason.Unknown;
            for (int index = 0; index < _roundParticipants.Count; index++)
                _roundParticipants[index].PrepareRound();

            TransitionTo(GameState.Playing);
            RoundStarted?.Invoke();
        }

        public void RestartRound()
        {
            ResetToWaitingState();
            StartRound();
        }

        public void ResumeRound()
        {
            if (CurrentState != GameState.Lost) return;

            CancelPendingHumanScream();
            LastFailureReason = GameFailureReason.Unknown;
            TransitionTo(GameState.Playing);
            for (int index = 0; index < _roundResumables.Count; index++)
                _roundResumables[index].ResumeRound();
        }

        /// <summary>
        /// Stops active round systems without publishing a failure result so a checkpoint
        /// owner can restore state and resume through the normal recovery path.
        /// </summary>
        public bool SuspendRoundForRestore()
        {
            if (CurrentState != GameState.Playing) return false;

            for (int index = 0; index < _roundParticipants.Count; index++)
                _roundParticipants[index].FinishRound(GameState.Lost);

            LastFailureReason = GameFailureReason.Unknown;
            TransitionTo(GameState.Lost);
            return true;
        }

        public void ResetToWaitingState()
        {
            CancelPendingHumanScream();
            if (CurrentState == GameState.Playing)
            {
                for (int index = 0; index < _roundParticipants.Count; index++)
                    _roundParticipants[index].FinishRound(GameState.Lost);
            }

            ResetConfiguredState();

            LastFailureReason = GameFailureReason.Unknown;
            TransitionTo(GameState.WaitingToStart);
        }

        private void ResetConfiguredState()
        {
            for (int index = 0; index < _resettables.Count; index++)
                _resettables[index].ResetState();

            // Publish every restored pose together before the waiting state, UI, or a
            // new round can observe stale transforms from the previous/edit-time pose.
            Physics.SyncTransforms();
        }

        [ContextMenu("Start Round")]
        private void StartRoundFromContextMenu()
        {
            StartRound();
        }

        [ContextMenu("Simulate Win")]
        private void SimulateWin()
        {
            HandleVictoryRequested();
        }

        [ContextMenu("Simulate Loss")]
        private void SimulateLoss()
        {
            HandleFailureRequested(GameFailureReason.Unknown);
        }

        private void HandleVictoryRequested()
        {
            CompleteRound(GameState.Won);
        }

        private void HandleFailureRequested(GameFailureReason reason)
        {
            LastFailureReason = reason;
            CompleteRound(GameState.Lost);
        }

        private void CompleteRound(GameState result)
        {
            if (CurrentState != GameState.Playing) return;
            if (result != GameState.Won && result != GameState.Lost) return;

            // Lock and finish gameplay before notifying presentation observers.
            for (int index = 0; index < _roundParticipants.Count; index++)
                _roundParticipants[index].FinishRound(result);

            TransitionTo(result);
            if (result == GameState.Won)
            {
                AudioManager.Instance?.Play(PalletStackerAudioKeys.Completed);
                GameWon?.Invoke();
            }
            else
            {
                ScheduleHumanScream(LastFailureReason);
                GameLost?.Invoke(LastFailureReason);
            }
        }

        private void ScheduleHumanScream(GameFailureReason reason)
        {
            if (reason != GameFailureReason.OperatorCollision &&
                reason != GameFailureReason.PedestrianCollision &&
                reason != GameFailureReason.PhoneDistractionCollision) return;

            CancelPendingHumanScream();
            _humanScreamRoutine = StartCoroutine(PlayHumanScreamAfterDelay());
        }

        private IEnumerator PlayHumanScreamAfterDelay()
        {
            float delay = Mathf.Max(0f, _humanScreamDelay);
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            _humanScreamRoutine = null;
            AudioManager.Instance?.Play(PalletStackerAudioKeys.HumanScream);
        }

        private void CancelPendingHumanScream()
        {
            if (_humanScreamRoutine == null) return;
            StopCoroutine(_humanScreamRoutine);
            _humanScreamRoutine = null;
        }

        private void TransitionTo(GameState nextState)
        {
            if (CurrentState == nextState) return;

            GameState previousState = CurrentState;
            CurrentState = nextState;
            StateChanged?.Invoke(previousState, nextState);
        }

        private void CacheComposition()
        {
            CacheInterfaces(_roundParticipantComponents, _roundParticipants, nameof(_roundParticipantComponents));
            CacheInterfaces(_victorySourceComponents, _victorySources, nameof(_victorySourceComponents));
            CacheInterfaces(_failureSourceComponents, _failureSources, nameof(_failureSourceComponents));
            CacheInterfaces(_resettableComponents, _resettables, nameof(_resettableComponents));

            _roundResumables.Clear();
            for (int index = 0; index < _roundParticipants.Count; index++)
            {
                if (_roundParticipants[index] is IGameRoundResumable resumable)
                    _roundResumables.Add(resumable);
            }
        }

        private void CacheInterfaces<T>(MonoBehaviour[] components, List<T> targets, string fieldName)
            where T : class
        {
            targets.Clear();
            if (components == null) return;

            for (int index = 0; index < components.Length; index++)
            {
                MonoBehaviour component = components[index];
                if (component == null) continue;

                if (!(component is T target))
                {
                    Debug.LogError(
                        $"'{component.name}' in {fieldName} does not implement {typeof(T).Name}.",
                        this);
                    continue;
                }

                if (!targets.Contains(target)) targets.Add(target);
            }
        }

        private void SubscribeSources()
        {
            if (_sourcesSubscribed) return;

            for (int index = 0; index < _victorySources.Count; index++)
                _victorySources[index].VictoryRequested += HandleVictoryRequested;
            for (int index = 0; index < _failureSources.Count; index++)
                _failureSources[index].FailureRequested += HandleFailureRequested;

            _sourcesSubscribed = true;
        }

        private void UnsubscribeSources()
        {
            if (!_sourcesSubscribed) return;

            for (int index = 0; index < _victorySources.Count; index++)
                _victorySources[index].VictoryRequested -= HandleVictoryRequested;
            for (int index = 0; index < _failureSources.Count; index++)
                _failureSources[index].FailureRequested -= HandleFailureRequested;

            _sourcesSubscribed = false;
        }
    }
}
