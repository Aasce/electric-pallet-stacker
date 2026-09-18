using System;

namespace ElectricPalletStackers.Gameplay
{
    public interface IGameRoundParticipant
    {
        void PrepareRound();
        void FinishRound(GameState result);
    }

    public interface IGameVictorySource
    {
        event Action VictoryRequested;
    }

    public interface IGameFailureSource
    {
        event Action FailureRequested;
    }
}
