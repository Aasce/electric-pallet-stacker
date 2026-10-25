using System;
using ElectricPalletStackers.PalletStackers;
using ElectricPalletStackers.UI;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    public enum TrainingExperienceStage
    {
        WaitingToStart,
        Start,
        PickupPallet,
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

        public void PrepareRound()
        {
            HasCompletedCrossSafety = false;
            _crossingVehicle?.ResetVehicle();
            _palletRoundController?.SetPalletVisible(true);
            TransitionTo(TrainingExperienceStage.Start);
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

            // Save the phone checkpoint before presenting the call so a reset retries
            // from this zone without replaying the completed Cross safety flow.
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

        public bool BeginPickupPalletPhase()
        {
            if (CurrentStage != TrainingExperienceStage.Start) return false;

            TransitionTo(TrainingExperienceStage.PickupPallet);
            return true;
        }

        public bool BeginCrossPhase()
        {
            if (CurrentStage != TrainingExperienceStage.Start &&
                CurrentStage != TrainingExperienceStage.PickupPallet) return false;

            TransitionTo(TrainingExperienceStage.YieldIntersection);
            return true;
        }

        public bool CompleteCrossPhaseWithoutSafetyCheck()
        {
            if (CurrentStage != TrainingExperienceStage.YieldIntersection) return false;

            TransitionTo(TrainingExperienceStage.PhoneIntersection);
            return true;
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
