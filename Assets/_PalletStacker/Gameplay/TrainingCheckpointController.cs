using System;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class TrainingCheckpointController : MonoBehaviour
    {
        [Header("Flow")]
        [SerializeField] private AppManager _appManager;
        [SerializeField] private TrainingExperienceController _experience;

        [Header("Snapshot Targets")]
        [SerializeField] private Rigidbody _vehicleBody;
        [SerializeField] private PalletStackerMast _mast;
        [SerializeField] private PalletStackerLoadHandler _loadHandler;
        [SerializeField] private PalletStackerLoad _pallet;
        [SerializeField] private PalletStackerRearUserFollower _playerFollower;

        private CheckpointSnapshot _latest;
        private bool _hasCheckpoint;
        private bool _isRestoring;

        public bool HasCheckpoint => _hasCheckpoint;
        public TrainingExperienceStage LatestStage => _latest.Stage;

        public event Action<TrainingExperienceStage> CheckpointSaved;
        public event Action<TrainingExperienceStage> CheckpointRestored;

        private void OnEnable()
        {
            _appManager.RoundStarted += HandleRoundStarted;
            _appManager.StateChanged += HandleGameStateChanged;
            _experience.StageChanged += HandleStageChanged;
        }

        private void OnDisable()
        {
            _appManager.RoundStarted -= HandleRoundStarted;
            _appManager.StateChanged -= HandleGameStateChanged;
            _experience.StageChanged -= HandleStageChanged;
        }

        public bool RestoreLatest()
        {
            if (!_hasCheckpoint) return false;

            _isRestoring = true;
            _loadHandler.ReleaseLoad();

            _vehicleBody.linearVelocity = Vector3.zero;
            _vehicleBody.angularVelocity = Vector3.zero;
            _vehicleBody.position = _latest.VehiclePosition;
            _vehicleBody.rotation = _latest.VehicleRotation;

            _mast.RestoreHeight(_latest.MastHeight, _latest.MastTargetHeight);

            Rigidbody palletBody = _pallet.Body;
            palletBody.linearVelocity = Vector3.zero;
            palletBody.angularVelocity = Vector3.zero;
            palletBody.position = _latest.PalletPosition;
            palletBody.rotation = _latest.PalletRotation;
            _pallet.ResetSupportTracking();

            Physics.SyncTransforms();
            if (_latest.HadAttachedLoad) _loadHandler.AttachLoad(_pallet);

            _experience.RestoreStage(_latest.Stage);
            _playerFollower.FollowVehicle();
            Physics.SyncTransforms();

            _isRestoring = false;
            CheckpointRestored?.Invoke(_latest.Stage);
            return true;
        }

        private void HandleRoundStarted()
        {
            Capture(_experience.CurrentStage);
        }

        private void HandleStageChanged(
            TrainingExperienceStage previous,
            TrainingExperienceStage next)
        {
            if (_isRestoring || _appManager.CurrentState != GameState.Playing) return;
            Capture(next);
        }

        private void HandleGameStateChanged(GameState previous, GameState next)
        {
            if (next == GameState.WaitingToStart) _hasCheckpoint = false;
        }

        private void Capture(TrainingExperienceStage stage)
        {
            Rigidbody palletBody = _pallet.Body;
            _latest = new CheckpointSnapshot(
                stage,
                _vehicleBody.position,
                _vehicleBody.rotation,
                _mast.CurrentHeight,
                _mast.TargetHeight,
                palletBody.position,
                palletBody.rotation,
                _loadHandler.HeldLoad == _pallet);
            _hasCheckpoint = true;
            CheckpointSaved?.Invoke(stage);
        }

        private readonly struct CheckpointSnapshot
        {
            public CheckpointSnapshot(
                TrainingExperienceStage stage,
                Vector3 vehiclePosition,
                Quaternion vehicleRotation,
                float mastHeight,
                float mastTargetHeight,
                Vector3 palletPosition,
                Quaternion palletRotation,
                bool hadAttachedLoad)
            {
                Stage = stage;
                VehiclePosition = vehiclePosition;
                VehicleRotation = vehicleRotation;
                MastHeight = mastHeight;
                MastTargetHeight = mastTargetHeight;
                PalletPosition = palletPosition;
                PalletRotation = palletRotation;
                HadAttachedLoad = hadAttachedLoad;
            }

            public TrainingExperienceStage Stage { get; }
            public Vector3 VehiclePosition { get; }
            public Quaternion VehicleRotation { get; }
            public float MastHeight { get; }
            public float MastTargetHeight { get; }
            public Vector3 PalletPosition { get; }
            public Quaternion PalletRotation { get; }
            public bool HadAttachedLoad { get; }
        }
    }
}
