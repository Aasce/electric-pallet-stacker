using System;
using ElectricPalletStackers.NPCs;
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
        [SerializeField] private NpcPopulationController _npcPopulation;
        [SerializeField] private CrossingVehicleController _crossingVehicle;

        [Header("Phase Reset Points")]
        [Tooltip("Optional authored vehicle poses for each stage. When a stage has no entry, the live checkpoint pose is used.")]
        [SerializeField] private PhaseResetPoint[] _phaseResetPoints;

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
            _experience.PalletRevealed += HandlePalletRevealed;
        }

        private void OnDisable()
        {
            _appManager.RoundStarted -= HandleRoundStarted;
            _appManager.StateChanged -= HandleGameStateChanged;
            _experience.StageChanged -= HandleStageChanged;
            _experience.PalletRevealed -= HandlePalletRevealed;
        }

        public bool RestoreLatest()
        {
            if (!_hasCheckpoint) return false;

            _isRestoring = true;
            _loadHandler.ReleaseLoad();

            RigidbodyPoseUtility.Restore(
                _vehicleBody,
                _latest.VehiclePosition,
                _latest.VehicleRotation,
                true);

            _mast.RestoreHeight(_latest.MastHeight, _latest.MastTargetHeight);

            if (_pallet.gameObject.activeSelf != _latest.PalletWasActive)
                _pallet.gameObject.SetActive(_latest.PalletWasActive);

            Rigidbody palletBody = _pallet.Body;
            Vector3 palletPosition = _latest.PalletPoseIsForkRelative
                ? _mast.Forks.TransformPoint(_latest.PalletPosition)
                : _latest.PalletPosition;
            Quaternion palletRotation = _latest.PalletPoseIsForkRelative
                ? _mast.Forks.rotation * _latest.PalletRotation
                : _latest.PalletRotation;
            RigidbodyPoseUtility.Restore(
                palletBody,
                palletPosition,
                palletRotation,
                false);
            _pallet.ResetSupportTracking();

            Physics.SyncTransforms();
            if (_latest.HadAttachedLoad && !_loadHandler.AttachLoad(_pallet))
                Debug.LogError("Failed to restore the checkpoint pallet attachment.", this);

            _crossingVehicle?.RestoreCheckpointState(_latest.CrossingVehicleState);
            _experience.RestoreStage(_latest.Stage);
            _npcPopulation?.RestoreCheckpoint();
            _playerFollower.FollowVehicle();
            Physics.SyncTransforms();

            _isRestoring = false;
            CheckpointRestored?.Invoke(_latest.Stage);
            return true;
        }

        public bool ResetCurrentPhase()
        {
            if (_appManager == null || _appManager.CurrentState != GameState.Playing)
                return false;
            if (!_hasCheckpoint || !_appManager.SuspendRoundForRestore()) return false;

            if (!RestoreLatest()) return false;
            _appManager.ResumeRound();
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

        private void HandlePalletRevealed()
        {
            if (_isRestoring || _appManager.CurrentState != GameState.Playing) return;
            if (_experience.CurrentStage == TrainingExperienceStage.NavigateCorner ||
                _experience.CurrentStage == TrainingExperienceStage.YieldIntersection) return;
            Capture(_experience.CurrentStage);
        }

        private void Capture(TrainingExperienceStage stage)
        {
            Transform resetPoint = GetResetPoint(stage);
            Vector3 vehiclePosition = resetPoint != null
                ? resetPoint.position
                : _vehicleBody.position;
            Quaternion vehicleRotation = resetPoint != null
                ? resetPoint.rotation
                : _vehicleBody.rotation;

            Rigidbody palletBody = _pallet.Body;
            bool hadAttachedLoad = _loadHandler.HeldLoad == _pallet;
            Transform forks = _mast.Forks;
            bool palletPoseIsForkRelative = hadAttachedLoad && forks != null;
            Vector3 palletPosition = palletPoseIsForkRelative
                ? forks.InverseTransformPoint(palletBody.position)
                : palletBody.position;
            Quaternion palletRotation = palletPoseIsForkRelative
                ? Quaternion.Inverse(forks.rotation) * palletBody.rotation
                : palletBody.rotation;
            _latest = new CheckpointSnapshot(
                stage,
                vehiclePosition,
                vehicleRotation,
                _mast.CurrentHeight,
                _mast.TargetHeight,
                palletPosition,
                palletRotation,
                hadAttachedLoad,
                palletPoseIsForkRelative,
                _pallet.gameObject.activeSelf,
                _crossingVehicle != null
                    ? _crossingVehicle.CaptureCheckpointState()
                    : default);
            _npcPopulation?.CaptureCheckpoint();
            _hasCheckpoint = true;
            CheckpointSaved?.Invoke(stage);
        }

        private Transform GetResetPoint(TrainingExperienceStage stage)
        {
            if (_phaseResetPoints == null) return null;

            for (int index = 0; index < _phaseResetPoints.Length; index++)
            {
                PhaseResetPoint entry = _phaseResetPoints[index];
                if (entry != null && entry.Stage == stage && entry.ResetPoint != null)
                    return entry.ResetPoint;
            }
            return null;
        }

        [Serializable]
        private sealed class PhaseResetPoint
        {
            [SerializeField] private TrainingExperienceStage _stage;
            [SerializeField] private Transform _resetPoint;

            public TrainingExperienceStage Stage => _stage;
            public Transform ResetPoint => _resetPoint;
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
                bool hadAttachedLoad,
                bool palletPoseIsForkRelative,
                bool palletWasActive,
                CrossingVehicleController.CheckpointState crossingVehicleState)
            {
                Stage = stage;
                VehiclePosition = vehiclePosition;
                VehicleRotation = vehicleRotation;
                MastHeight = mastHeight;
                MastTargetHeight = mastTargetHeight;
                PalletPosition = palletPosition;
                PalletRotation = palletRotation;
                HadAttachedLoad = hadAttachedLoad;
                PalletPoseIsForkRelative = palletPoseIsForkRelative;
                PalletWasActive = palletWasActive;
                CrossingVehicleState = crossingVehicleState;
            }

            public TrainingExperienceStage Stage { get; }
            public Vector3 VehiclePosition { get; }
            public Quaternion VehicleRotation { get; }
            public float MastHeight { get; }
            public float MastTargetHeight { get; }
            public Vector3 PalletPosition { get; }
            public Quaternion PalletRotation { get; }
            public bool HadAttachedLoad { get; }
            public bool PalletPoseIsForkRelative { get; }
            public bool PalletWasActive { get; }
            public CrossingVehicleController.CheckpointState CrossingVehicleState { get; }
        }
    }
}
