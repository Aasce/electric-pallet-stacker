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

        public TrainingExperienceStage CurrentStage { get; private set; } =
            TrainingExperienceStage.WaitingToStart;
        public bool HasCompletedCrossSafety { get; private set; }
        public event Action VictoryRequested;
        public event Action<TrainingExperienceStage, TrainingExperienceStage> StageChanged;

        private void OnEnable()
        {
            if (_palletRoundController != null)
                _palletRoundController.VictoryRequested += HandleDestinationConfirmed;
            if (_phoneCallEvent != null)
                _phoneCallEvent.CallCompleted += HandlePhoneCallCompleted;
        }

        private void OnDisable()
        {
            if (_palletRoundController != null)
                _palletRoundController.VictoryRequested -= HandleDestinationConfirmed;
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
            HasCompletedCrossSafety = false;
            _crossingVehicle?.ResetVehicle();
            _palletRoundController?.SetPalletVisible(true);
            // The authored route starts in the NPC Moving area. CollectPallet is a
            // legacy stage from the old route, so the experience begins at the corner.
            TransitionTo(TrainingExperienceStage.NavigateCorner);
        }

        public void ResumeRound()
        {
            switch (CurrentStage)
            {
                case TrainingExperienceStage.PhoneCall:
                    RestartPhoneCallPhase();
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
            HasCompletedCrossSafety = false;
            _crossingVehicle?.ResetVehicle();
            _phoneCallEvent?.ResetState();
            _palletRoundController?.SetPalletVisible(true);
            TransitionTo(TrainingExperienceStage.WaitingToStart);
        }

        public void RestoreStage(TrainingExperienceStage stage)
        {
            _phoneCallEvent?.ResetState();
            // An interrupted call must be retried from the approach to its zone.
            TransitionTo(stage == TrainingExperienceStage.PhoneCall
                ? TrainingExperienceStage.PhoneIntersection
                : stage);
        }

        public bool BeginPhoneCall()
        {
            if (CurrentStage != TrainingExperienceStage.PhoneIntersection) return false;

            // Save the phone checkpoint before spawning the distraction NPC. The
            // dedicated Cross flow can reach this zone before the legacy waypoints.
            TransitionTo(TrainingExperienceStage.PhoneCall);
            _phoneCallEvent?.RestartIncomingCall();
            return true;
        }

        public void CompleteCrossSafetyCheck()
        {
            if (CurrentStage != TrainingExperienceStage.YieldIntersection) return;

            HasCompletedCrossSafety = true;
            _crossingVehicle?.SuppressVehicle();
            TransitionTo(TrainingExperienceStage.PhoneIntersection);
        }

        public bool CompleteNpcMovingPhase()
        {
            if (CurrentStage != TrainingExperienceStage.NavigateCorner) return false;

            TransitionTo(TrainingExperienceStage.YieldIntersection);
            return true;
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
                    return BeginPhoneCall();
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

        private void RestartPhoneCallPhase()
        {
            _phoneCallEvent?.RestartIncomingCall();
            if (CurrentStage == TrainingExperienceStage.PhoneIntersection)
                TransitionTo(TrainingExperienceStage.PhoneCall);
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
