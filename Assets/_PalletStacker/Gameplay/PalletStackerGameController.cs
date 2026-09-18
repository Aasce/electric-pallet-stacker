using System;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerGameController : MonoBehaviour, IGameRoundParticipant, IGameFailureSource
    {
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;
        [SerializeField] private PalletStackerControlDriver _controlDriver;

        public event Action FailureRequested;

        private void OnEnable()
        {
            if (_collisionReporter != null)
                _collisionReporter.CollisionDetected += HandleCollisionDetected;
        }

        private void OnDisable()
        {
            if (_collisionReporter != null)
                _collisionReporter.CollisionDetected -= HandleCollisionDetected;
        }

        public void PrepareRound()
        {
            if (_controlDriver == null) return;

            _controlDriver.ClearCollisionInterlock();
            _controlDriver.SetGameplayInterlock(false);
        }

        public void FinishRound(GameState result)
        {
            _controlDriver?.SetGameplayInterlock(true);
        }

        private void HandleCollisionDetected()
        {
            // Stop locally before the AppManager broadcasts the result.
            _controlDriver?.SetGameplayInterlock(true);
            FailureRequested?.Invoke();
        }
    }
}
