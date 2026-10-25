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
        [SerializeField] private PalletStackerRigidbodyMotor _vehicleMotor;
        [SerializeField] private PalletStackerMast _mast;
        [SerializeField] private PalletStackerLoadHandler _loadHandler;
        [SerializeField] private PalletStackerLoad _pallet;
        [SerializeField] private PalletStackerRearUserFollower _playerFollower;
        [SerializeField] private CrossingVehicleController _crossingVehicle;

        [Header("Phase Reset Points")]
        [Tooltip("Optional authored vehicle poses for each stage. When a stage has no entry, the live checkpoint pose is used.")]
        [SerializeField] private PhaseResetPoint[] _phaseResetPoints;

        [Header("Direction-Aware Restore")]
        [Tooltip("Minimum signed vehicle speed used to determine whether the vehicle entered a phase forwards or in reverse.")]
        [SerializeField, Min(0f)] private float _travelDirectionThreshold = 0.01f;

        private CheckpointSnapshot _latest;
        private bool _hasCheckpoint;
        private bool _isRestoring;
        private bool _hasCheckpointEntryDirection;
        private bool _checkpointEnteredInReverse;
        private bool _isWaitingForEntryDirection;
        private bool _palletReleasedThisFrame;
        private Vector3 _releasedPalletPositionInForkSpace;
        private Quaternion _releasedPalletRotationInForkSpace;
        private bool _hasFailurePalletSnapshot;
        private Vector3 _failurePalletPositionInForkSpace;
        private Quaternion _failurePalletRotationInForkSpace;

        public bool HasCheckpoint => _hasCheckpoint;
        public TrainingExperienceStage LatestStage => _latest.Stage;

        public event Action<TrainingExperienceStage> CheckpointSaved;
        public event Action<TrainingExperienceStage> CheckpointRestored;

        private void OnEnable()
        {
            _appManager.RoundStarted += HandleRoundStarted;
            _appManager.GameLost += HandleGameLost;
            _appManager.StateChanged += HandleGameStateChanged;
            _experience.StageChanged += HandleStageChanged;
            _loadHandler.LoadReleased += HandleLoadReleased;
        }

        private void OnDisable()
        {
            _appManager.RoundStarted -= HandleRoundStarted;
            _appManager.GameLost -= HandleGameLost;
            _appManager.StateChanged -= HandleGameStateChanged;
            _experience.StageChanged -= HandleStageChanged;
            _loadHandler.LoadReleased -= HandleLoadReleased;
        }

        private void Update()
        {
            // A normal pallet release should not be mistaken for a load that was
            // dropped by the failure which ended the round.
            _palletReleasedThisFrame = false;

            if (!_isWaitingForEntryDirection ||
                _appManager == null ||
                _appManager.CurrentState != GameState.Playing) return;

            if (TryGetEnteredInReverse(out bool enteredInReverse))
                SaveCheckpointEntryDirection(enteredInReverse);
        }

        /// <summary>
        /// Freezes the forward/reverse mode the first time the vehicle enters the
        /// current phase. If it is stationary on the trigger frame, capture is deferred
        /// until the first actual movement rather than guessing a direction.
        /// </summary>
        public void CapturePhaseEntryDirection()
        {
            if (!_hasCheckpoint || _hasCheckpointEntryDirection) return;

            if (TryGetEnteredInReverse(out bool enteredInReverse))
                SaveCheckpointEntryDirection(enteredInReverse);
            else
                _isWaitingForEntryDirection = true;
        }

        public bool RestoreLatest()
        {
            if (!_hasCheckpoint) return false;

            // Save the live fork-relative pose before releasing the load and moving
            // the vehicle back to the phase checkpoint. A failed phase can have
            // picked up the pallet after its checkpoint was captured.
            bool preserveCurrentlyCarriedPallet = TryGetCarriedPalletPose(
                out Vector3 carriedPalletPosition,
                out Quaternion carriedPalletRotation);
            bool preserveFailurePallet = !preserveCurrentlyCarriedPallet &&
                _appManager.CurrentState == GameState.Lost && _hasFailurePalletSnapshot;
            bool preservePallet = preserveCurrentlyCarriedPallet || preserveFailurePallet;
            Vector3 preservedPalletPosition = preserveCurrentlyCarriedPallet
                ? carriedPalletPosition
                : _failurePalletPositionInForkSpace;
            Quaternion preservedPalletRotation = preserveCurrentlyCarriedPallet
                ? carriedPalletRotation
                : _failurePalletRotationInForkSpace;

            _isRestoring = true;
            _loadHandler.ReleaseLoad();

            RigidbodyPoseUtility.Restore(
                _vehicleBody,
                _latest.VehiclePosition,
                GetRestoreVehicleRotation(),
                true);

            _mast.RestoreHeight(_latest.MastHeight, _latest.MastTargetHeight);

            bool palletWasActive = preservePallet || _latest.PalletWasActive;
            if (_pallet.gameObject.activeSelf != palletWasActive)
                _pallet.gameObject.SetActive(palletWasActive);
            _pallet.SetHighlighted(preservePallet ? false : _latest.PalletWasHighlighted);

            Rigidbody palletBody = _pallet.Body;
            Vector3 palletPosition = preservePallet
                ? _mast.Forks.TransformPoint(preservedPalletPosition)
                : _latest.PalletPoseIsForkRelative
                    ? _mast.Forks.TransformPoint(_latest.PalletPosition)
                    : _latest.PalletPosition;
            Quaternion palletRotation = preservePallet
                ? _mast.Forks.rotation * preservedPalletRotation
                : _latest.PalletPoseIsForkRelative
                    ? _mast.Forks.rotation * _latest.PalletRotation
                    : _latest.PalletRotation;
            RigidbodyPoseUtility.Restore(
                palletBody,
                palletPosition,
                palletRotation,
                false);
            _pallet.ResetSupportTracking();

            Physics.SyncTransforms();
            bool shouldAttachPallet = preservePallet || _latest.HadAttachedLoad;
            if (shouldAttachPallet && !_loadHandler.AttachLoad(_pallet))
                Debug.LogError("Failed to restore the checkpoint pallet attachment.", this);

            _crossingVehicle?.RestoreCheckpointState(_latest.CrossingVehicleState);
            _experience.RestoreStage(_latest.Stage);
            _playerFollower.FollowVehicle();
            Physics.SyncTransforms();

            _isRestoring = false;
            _hasFailurePalletSnapshot = false;
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
            _hasFailurePalletSnapshot = false;
            _palletReleasedThisFrame = false;
            _hasCheckpointEntryDirection = false;
            _checkpointEnteredInReverse = false;
            _isWaitingForEntryDirection = false;
            Capture(_experience.CurrentStage);
        }

        private void HandleLoadReleased(PalletStackerLoad releasedLoad)
        {
            if (_isRestoring || releasedLoad != _pallet ||
                !TryGetPalletPoseRelativeToForks(
                    out _releasedPalletPositionInForkSpace,
                    out _releasedPalletRotationInForkSpace)) return;

            _palletReleasedThisFrame = true;
        }

        private void HandleGameLost(GameFailureReason reason)
        {
            if (!_palletReleasedThisFrame) return;

            _hasFailurePalletSnapshot = true;
            _failurePalletPositionInForkSpace = _releasedPalletPositionInForkSpace;
            _failurePalletRotationInForkSpace = _releasedPalletRotationInForkSpace;
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
            if (next != GameState.WaitingToStart) return;

            _hasCheckpoint = false;
            _hasFailurePalletSnapshot = false;
            _palletReleasedThisFrame = false;
            _hasCheckpointEntryDirection = false;
            _checkpointEnteredInReverse = false;
            _isWaitingForEntryDirection = false;
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

            _hasCheckpointEntryDirection = TryGetEnteredInReverse(
                out _checkpointEnteredInReverse);
            _isWaitingForEntryDirection = !_hasCheckpointEntryDirection;
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
                _pallet.IsHighlighted,
                _crossingVehicle != null
                    ? _crossingVehicle.CaptureCheckpointState()
                    : default);
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

        private Quaternion GetRestoreVehicleRotation()
        {
            Quaternion rotation = _latest.VehicleRotation;
            if (!_hasCheckpointEntryDirection || !_checkpointEnteredInReverse ||
                GetResetPoint(_latest.Stage) == null) return rotation;

            return Quaternion.AngleAxis(180f, Vector3.up) * rotation;
        }

        private bool TryGetEnteredInReverse(out bool enteredInReverse)
        {
            enteredInReverse = false;
            if (_vehicleMotor != null)
            {
                float signedSpeed = _vehicleMotor.CurrentSpeed;
                if (Mathf.Abs(signedSpeed) <= _travelDirectionThreshold) return false;

                enteredInReverse = signedSpeed < 0f;
                return true;
            }

            if (_vehicleBody == null) return false;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(
                _vehicleBody.linearVelocity,
                Vector3.up);
            if (planarVelocity.sqrMagnitude <=
                _travelDirectionThreshold * _travelDirectionThreshold) return false;

            Vector3 vehicleForward = Vector3.ProjectOnPlane(
                _vehicleBody.transform.forward,
                Vector3.up);
            if (vehicleForward.sqrMagnitude <= Mathf.Epsilon) return false;

            enteredInReverse = Vector3.Dot(planarVelocity, vehicleForward) < 0f;
            return true;
        }

        private void SaveCheckpointEntryDirection(bool enteredInReverse)
        {
            _checkpointEnteredInReverse = enteredInReverse;
            _hasCheckpointEntryDirection = true;
            _isWaitingForEntryDirection = false;
        }

        private bool TryGetCarriedPalletPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            return _loadHandler != null && _loadHandler.HeldLoad == _pallet &&
                   TryGetPalletPoseRelativeToForks(out position, out rotation);
        }

        private bool TryGetPalletPoseRelativeToForks(
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (_pallet == null || _pallet.Body == null || _mast == null || _mast.Forks == null)
                return false;

            Transform forks = _mast.Forks;
            position = forks.InverseTransformPoint(_pallet.Body.position);
            rotation = Quaternion.Inverse(forks.rotation) * _pallet.Body.rotation;
            return true;
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
                bool palletWasHighlighted,
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
                PalletWasHighlighted = palletWasHighlighted;
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
            public bool PalletWasHighlighted { get; }
            public CrossingVehicleController.CheckpointState CrossingVehicleState { get; }
        }
    }
}
