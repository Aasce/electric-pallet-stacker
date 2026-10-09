using System;
using ElectricPalletStackers.PalletStackers;
using ElectricPalletStackers.UI;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    public enum TrainingExperienceStage
    {
        WaitingToStart,
        CollectPallet,
        NavigateCorner,
        YieldIntersection,
        PhoneIntersection,
        DeliverPallet,
        Completed
    }

    [DisallowMultipleComponent]
    public sealed class TrainingExperienceController : MonoBehaviour,
        IGameRoundParticipant,
        IGameVictorySource,
        IGameResettable
    {
        [SerializeField] private PalletRoundController _palletRoundController;
        [SerializeField] private PalletStackerLoadHandler _loadHandler;
        [SerializeField] private CrossingVehicleController _crossingVehicle;
        [SerializeField] private PhoneCallEventSimulator _phoneCallEvent;
        [SerializeField] private ExperienceCheckpointTrigger[] _checkpointTriggers;

        public TrainingExperienceStage CurrentStage { get; private set; } =
            TrainingExperienceStage.WaitingToStart;
        public event Action VictoryRequested;
        public event Action<TrainingExperienceStage, TrainingExperienceStage> StageChanged;

        private void OnEnable()
        {
            if (_palletRoundController != null)
                _palletRoundController.VictoryRequested += HandleDestinationConfirmed;
        }

        private void OnDisable()
        {
            if (_palletRoundController != null)
                _palletRoundController.VictoryRequested -= HandleDestinationConfirmed;
        }

        private void Update()
        {
            if (CurrentStage == TrainingExperienceStage.CollectPallet &&
                _loadHandler != null && _loadHandler.HeldLoad != null)
                TransitionTo(TrainingExperienceStage.NavigateCorner);
        }

        public void PrepareRound()
        {
            ResetCheckpoints();
            _crossingVehicle?.ResetVehicle();
            TransitionTo(TrainingExperienceStage.CollectPallet);
        }

        public void FinishRound(GameState result)
        {
            if (result == GameState.Won) TransitionTo(TrainingExperienceStage.Completed);
        }

        public void ResetState()
        {
            ResetCheckpoints();
            _crossingVehicle?.ResetVehicle();
            _phoneCallEvent?.ResetState();
            TransitionTo(TrainingExperienceStage.WaitingToStart);
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
                    _palletRoundController?.ArmDestination();
                    TransitionTo(TrainingExperienceStage.DeliverPallet);
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

        private void ResetCheckpoints()
        {
            if (_checkpointTriggers == null) return;
            for (int index = 0; index < _checkpointTriggers.Length; index++)
                _checkpointTriggers[index]?.ResetTrigger();
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
