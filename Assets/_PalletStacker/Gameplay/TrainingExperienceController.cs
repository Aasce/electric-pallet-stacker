using System;
using ElectricPalletStackers.PalletStackers;
using ElectricPalletStackers.UI;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    public enum ExperienceCheckpoint
    {
        Corner,
        YieldIntersection,
        PhoneIntersection
    }

    public enum TrainingExperienceStage
    {
        WaitingToStart,
        CollectPallet,
        NavigateCorner,
        YieldIntersection,
        PhoneIntersection,
        PhoneCall,
        DeliverPallet,
        Completed
    }

    [DisallowMultipleComponent]
    public sealed class TrainingExperienceController : MonoBehaviour,
        IGameRoundParticipant,
        IGameRoundResumable,
        IGameVictorySource,
        IGameResettable
    {
        [Header("Scene Dependencies")]
        [Tooltip("Round coordinator assigned from the scene Gameplay Manager.")]
        [SerializeField] private PalletRoundController _palletRoundController;
        [Tooltip("Load handler on the player pallet stacker instance.")]
        [SerializeField] private PalletStackerLoadHandler _loadHandler;
        [Tooltip("Crossing vehicle controller owned by the Cross phase.")]
        [SerializeField] private CrossingVehicleController _crossingVehicle;
        [Tooltip("Phone event service assigned from the scene Gameplay Manager.")]
        [SerializeField] private PhoneCallEventSimulator _phoneCallEvent;
        [Tooltip("The crossing-vehicle waypoint that reveals the training pallet. Zero-based; Waypoint 03 is index 2.")]
        [SerializeField, Min(1)] private int _palletRevealWaypointIndex = 2;

        public TrainingExperienceStage CurrentStage { get; private set; } =
            TrainingExperienceStage.WaitingToStart;
        public event Action VictoryRequested;
        public event Action PalletRevealed;
        public event Action<TrainingExperienceStage, TrainingExperienceStage> StageChanged;

        private void OnEnable()
        {
            if (_palletRoundController != null)
                _palletRoundController.VictoryRequested += HandleDestinationConfirmed;
            if (_crossingVehicle != null)
                _crossingVehicle.WaypointReached += HandleCrossingWaypointReached;
            if (_phoneCallEvent != null)
                _phoneCallEvent.CallCompleted += HandlePhoneCallCompleted;
        }

        private void OnDisable()
        {
            if (_palletRoundController != null)
                _palletRoundController.VictoryRequested -= HandleDestinationConfirmed;
            if (_crossingVehicle != null)
                _crossingVehicle.WaypointReached -= HandleCrossingWaypointReached;
            if (_phoneCallEvent != null)
                _phoneCallEvent.CallCompleted -= HandlePhoneCallCompleted;
        }

        private void Update()
        {
            if (CurrentStage == TrainingExperienceStage.CollectPallet &&
                _loadHandler != null && _loadHandler.HeldLoad != null)
                TransitionTo(TrainingExperienceStage.NavigateCorner);
        }

        public void PrepareRound()
        {
            _crossingVehicle?.ResetVehicle();
            _palletRoundController?.SetPalletVisible(false);
            TransitionTo(TrainingExperienceStage.CollectPallet);
        }

        public void ResumeRound()
        {
            switch (CurrentStage)
            {
                case TrainingExperienceStage.PhoneIntersection:
                    break;
                case TrainingExperienceStage.PhoneCall:
                    _phoneCallEvent.RestartIncomingCall();
                    break;
                case TrainingExperienceStage.DeliverPallet:
                    _palletRoundController.ArmDestination();
                    break;
            }
        }

        public void FinishRound(GameState result)
        {
            if (result == GameState.Won) TransitionTo(TrainingExperienceStage.Completed);
        }

        public void ResetState()
        {
            _crossingVehicle?.ResetVehicle();
            _phoneCallEvent?.ResetState();
            _palletRoundController?.SetPalletVisible(false);
            TransitionTo(TrainingExperienceStage.WaitingToStart);
        }

        public void RestoreStage(TrainingExperienceStage stage)
        {
            _phoneCallEvent.ResetState();
            TransitionTo(stage);
        }

        public bool ReachCheckpoint(ExperienceCheckpoint checkpoint)
        {
            switch (checkpoint)
            {
                case ExperienceCheckpoint.Corner
                    when CurrentStage == TrainingExperienceStage.NavigateCorner:
                    TransitionTo(TrainingExperienceStage.YieldIntersection);
                    return true;

                case ExperienceCheckpoint.YieldIntersection
                    when CurrentStage == TrainingExperienceStage.YieldIntersection:
                    _crossingVehicle?.BeginCrossing();
                    TransitionTo(TrainingExperienceStage.PhoneIntersection);
                    return true;

                case ExperienceCheckpoint.PhoneIntersection
                    when CurrentStage == TrainingExperienceStage.PhoneIntersection:
                    _phoneCallEvent?.TriggerIncomingCall();
                    TransitionTo(TrainingExperienceStage.PhoneCall);
                    return true;
            }
            return false;
        }

        private void HandleDestinationConfirmed()
        {
            if (CurrentStage != TrainingExperienceStage.DeliverPallet) return;
            TransitionTo(TrainingExperienceStage.Completed);
            VictoryRequested?.Invoke();
        }

        private void HandlePhoneCallCompleted()
        {
            if (CurrentStage != TrainingExperienceStage.PhoneCall) return;
            _palletRoundController?.ArmDestination();
            TransitionTo(TrainingExperienceStage.DeliverPallet);
        }

        private void HandleCrossingWaypointReached(int waypointIndex)
        {
            if (waypointIndex != _palletRevealWaypointIndex) return;
            _palletRoundController?.SetPalletVisible(true);
            PalletRevealed?.Invoke();
        }

        private void TransitionTo(TrainingExperienceStage next)
        {
            if (CurrentStage == next) return;
            TrainingExperienceStage previous = CurrentStage;
            CurrentStage = next;
            StageChanged?.Invoke(previous, next);
        }
    }
}
