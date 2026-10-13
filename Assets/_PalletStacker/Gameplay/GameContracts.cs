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

    public interface IGameResettable
    {
        void ResetState();
    }
}
