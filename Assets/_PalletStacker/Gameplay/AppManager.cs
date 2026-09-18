using System;
using System.Collections.Generic;
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

        [Header("Startup")]
        [Tooltip("Leave disabled when UIFlowGameStarter starts the round after the guide UI closes.")]
        [SerializeField] private bool _autoStart;

        private readonly List<IGameRoundParticipant> _roundParticipants = new();
        private readonly List<IGameVictorySource> _victorySources = new();
        private readonly List<IGameFailureSource> _failureSources = new();
        private bool _sourcesSubscribed;

        public GameState CurrentState { get; private set; } = GameState.Initializing;

        public event Action<GameState, GameState> StateChanged;
        public event Action RoundStarted;
        public event Action GameWon;
        public event Action GameLost;

        private void Awake()
        {
            CacheComposition();
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
        }

        public void StartRound()
        {
            if (CurrentState == GameState.Playing) return;

            for (int index = 0; index < _roundParticipants.Count; index++)
                _roundParticipants[index].PrepareRound();

            TransitionTo(GameState.Playing);
            RoundStarted?.Invoke();
        }

        public void RestartRound()
        {
            StartRound();
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
            HandleFailureRequested();
        }

        private void HandleVictoryRequested()
        {
            CompleteRound(GameState.Won);
        }

        private void HandleFailureRequested()
        {
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
            if (result == GameState.Won) GameWon?.Invoke();
            else GameLost?.Invoke();
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
