using System;

namespace ElectricPalletStackers.Gameplay
{
    public interface IGameRoundParticipant
    {
        void PrepareRound();
        void FinishRound(GameState result);
    }

    public interface IGameRoundResumable
    {
        void ResumeRound();
    }

    public interface IGameVictorySource
    {
        event Action VictoryRequested;
    }

    public interface IGameFailureSource
    {
        event Action<GameFailureReason> FailureRequested;
    }

    /// <summary>
    /// Supplies the safety-specific reason for a collision target. Collision detection
    /// remains independent from the systems that own pedestrians, traffic, or hazards.
    /// </summary>
    public interface IGameFailureReasonProvider
    {
        GameFailureReason FailureReason { get; }
    }

    public interface IGameResettable
    {
        void ResetState();
    }
}
