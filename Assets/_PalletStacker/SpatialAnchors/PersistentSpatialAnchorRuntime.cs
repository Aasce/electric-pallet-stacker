using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ElectricPalletStackers.SpatialAnchors
{
    /// <summary>
    /// Saves one Quest spatial anchor at a physical reference point and restores the
    /// environment/gameplay roots relative to it on later launches.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PersistentSpatialAnchorRuntime : MonoBehaviour
    {
        private const string AnchorGuidKey = "ElectricPalletStacker.SpatialAnchor.Guid";
        private const string RelativePosesKey = "ElectricPalletStacker.SpatialAnchor.RootPoses";
        private const float TriggerThreshold = 0.75f;
        private const float ReplaceHoldSeconds = 2f;
        private const float SessionReadyTimeoutSeconds = 12f;
        private const float AnchorTrackingTimeoutSeconds = 12f;

        private static readonly string[] ContentRootNames =
        {
            "Enviroments",
            "Gameplay",
            "Navmesh Surface"
        };

        [SerializeField] private bool _showSetupOverlay = true;

        private XROrigin _xrOrigin;
        private ARSession _arSession;
        private ARAnchorManager _anchorManager;
        private Transform[] _contentRoots;
        private Canvas _statusCanvas;
        private TextMeshProUGUI _statusText;
        private GameObject _posePreview;
        private LineRenderer _posePreviewLine;
        private bool _setupActive;
        private bool _busy;
        private bool _triggerWasPressed;
        private bool _replacementGestureConsumed;
        private bool _replacementAllowed;
        private float _replacementHoldTime;
        private string _previousAnchorGuid;

        public event Action ReadyForApp;
        public event Action PlacementStarted;

        private void Start()
        {
            _xrOrigin = FindAnyObjectByType<XROrigin>();
            if (_xrOrigin == null)
            {
                SetStatus("XR Origin not found. Spatial anchor setup cannot start.");
                return;
            }

            _contentRoots = FindContentRoots();
            if (_contentRoots.Length == 0)
            {
                SetStatus("No world content roots were found to anchor.");
                return;
            }

            _xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            EnsureAnchorComponents();
            CreateStatusOverlay();
            CreatePosePreview();
            SetStatus("Starting Quest spatial anchor… / Đang khởi động spatial anchor…");
            _ = InitializeAsync();
        }

        private void Update()
        {
            if (_xrOrigin == null) return;

            InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool hasPose = TryGetRightControllerWorldPose(rightController, out Pose controllerPose);

            if (_setupActive && hasPose)
                UpdatePosePreview(controllerPose);
            else if (_posePreview != null)
                _posePreview.SetActive(false);

            bool triggerPressed = rightController.isValid &&
                                  rightController.TryGetFeatureValue(CommonUsages.trigger, out float trigger) &&
                                  trigger >= TriggerThreshold;

            if (_setupActive && !_busy && hasPose && triggerPressed && !_triggerWasPressed)
                _ = CreateAndSaveAnchorAsync(controllerPose);

            _triggerWasPressed = triggerPressed;

            bool secondaryPressed = rightController.isValid &&
                                    rightController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondary) &&
                                    secondary;
            if (!_setupActive && !_busy && _replacementAllowed && secondaryPressed)
            {
                _replacementHoldTime += Time.unscaledDeltaTime;
                if (_replacementHoldTime >= ReplaceHoldSeconds && !_replacementGestureConsumed)
                {
                    _replacementGestureConsumed = true;
                    BeginPlacement("Replace the saved anchor: hold the right controller at the fixed truck/floor mark, align it with the truck, then press the trigger.\n" +
                                    "Tạo lại anchor: đặt tay cầm tại mốc cố định trên xe/sàn, hướng theo xe rồi bóp cò.");
                }
            }
            else
            {
                _replacementHoldTime = 0f;
                if (!secondaryPressed) _replacementGestureConsumed = false;
            }
        }

        private void OnDestroy()
        {
            if (_statusCanvas != null) Destroy(_statusCanvas.gameObject);
            if (_posePreview != null) Destroy(_posePreview);
        }

        public void SetReplacementAllowed(bool allowed)
        {
            _replacementAllowed = allowed;
        }

        private async Task InitializeAsync()
        {
            bool sessionReady = await WaitForAnchorSubsystemAsync();
            if (!sessionReady)
            {
                BeginPlacement("Quest anchor subsystem is unavailable. Build to Quest with Meta Quest: Session and Meta Quest: Anchors enabled.\n" +
                                "Không tìm thấy anchor subsystem. Hãy build lên Quest và bật Meta Quest: Session + Meta Quest: Anchors.");
                return;
            }

            string savedGuid = PlayerPrefs.GetString(AnchorGuidKey, string.Empty);
            if (!Guid.TryParse(savedGuid, out Guid parsedGuid))
            {
                BeginPlacement("Place the right controller at a fixed point on the real truck or floor. Point it along the truck, then press the trigger to save.\n" +
                                "Đặt tay cầm phải tại mốc cố định trên xe thật hoặc sàn. Hướng theo xe rồi bóp cò để lưu.");
                return;
            }

            SetStatus("Finding the saved anchor… / Đang tìm anchor đã lưu…");
            _busy = true;
            try
            {
                ARAnchor anchor = await LoadAnchorAsync(new SerializableGuid(parsedGuid));
                if (anchor == null)
                {
                    BeginPlacement("Saved anchor could not be localized. Set the controller at the fixed truck/floor mark and press the trigger to replace it.\n" +
                                    "Không định vị được anchor. Đặt tay cầm tại mốc cố định trên xe/sàn rồi bóp cò để tạo lại.");
                    return;
                }

                ApplySavedRootPoses(anchor.transform);
                SetStatus("Spatial anchor restored. / Đã khôi phục spatial anchor.");
                await Task.Delay(1200);
                HideStatus();
                ReadyForApp?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                BeginPlacement("Could not load the saved anchor. Place the controller at the fixed mark and press the trigger to set a new one.\n" +
                                "Không tải được anchor. Đặt tay cầm tại mốc cố định rồi bóp cò để tạo anchor mới.");
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task<bool> WaitForAnchorSubsystemAsync()
        {
            float elapsed = 0f;
            while (elapsed < SessionReadyTimeoutSeconds)
            {
                if (_anchorManager != null && _anchorManager.subsystem != null &&
                    _anchorManager.subsystem.running)
                    return true;

                await Awaitable.NextFrameAsync();
                elapsed += Time.unscaledDeltaTime;
            }

            return false;
        }

        private async Task<ARAnchor> LoadAnchorAsync(SerializableGuid guid)
        {
            var result = await _anchorManager.TryLoadAnchorAsync(guid);
            if (!result.status.IsSuccess())
                return null;

            ARAnchor anchor = result.value;
            float elapsed = 0f;
            while (anchor != null && anchor.trackingState != TrackingState.Tracking &&
                   elapsed < AnchorTrackingTimeoutSeconds)
            {
                await Awaitable.NextFrameAsync();
                elapsed += Time.unscaledDeltaTime;
            }

            return anchor != null && anchor.trackingState == TrackingState.Tracking ? anchor : null;
        }

        private async Task CreateAndSaveAnchorAsync(Pose controllerPose)
        {
            if (_busy || !_setupActive) return;
            _busy = true;
            SetStatus("Creating and saving anchor… / Đang tạo và lưu anchor…");

            Vector3 forward = Vector3.ProjectOnPlane(controllerPose.rotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.forward : Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            Pose anchorPose = new(controllerPose.position, Quaternion.LookRotation(forward.normalized, Vector3.up));
            try
            {
                var createResult = await _anchorManager.TryAddAnchorAsync(anchorPose);
                if (!createResult.status.IsSuccess() || createResult.value == null)
                {
                    SetStatus("Anchor creation failed. Keep the controller tracked and press the trigger to retry.\n" +
                              "Tạo anchor thất bại. Giữ tay cầm trong vùng tracking và bóp cò để thử lại.");
                    return;
                }

                ARAnchor anchor = createResult.value;
                bool localized = await WaitUntilTrackedAsync(anchor);
                if (!localized)
                {
                    SetStatus("Anchor was created but is not localized yet. Keep the headset still and retry.\n" +
                              "Anchor đã tạo nhưng chưa định vị xong. Giữ headset ổn định rồi thử lại.");
                    return;
                }

                var saveResult = await _anchorManager.TrySaveAnchorAsync(anchor);
                if (!saveResult.status.IsSuccess())
                {
                    SetStatus("Anchor was created but could not be saved. Press the trigger to retry.\n" +
                              "Đã tạo anchor nhưng chưa lưu được. Bóp cò để thử lại.");
                    return;
                }

                _previousAnchorGuid = PlayerPrefs.GetString(AnchorGuidKey, string.Empty);
                StoreRootPoses(anchor.transform);
                PlayerPrefs.SetString(AnchorGuidKey, saveResult.value.guid.ToString("D"));
                PlayerPrefs.Save();

                if (Guid.TryParse(_previousAnchorGuid, out Guid previousGuid) && previousGuid != saveResult.value.guid.guid)
                    await _anchorManager.TryEraseAnchorAsync(new SerializableGuid(previousGuid));

                _setupActive = false;
                if (_posePreview != null) _posePreview.SetActive(false);
                SetStatus("Anchor saved. / Đã lưu anchor.");
                await Task.Delay(900);
                HideStatus();
                ReadyForApp?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                SetStatus("Anchor operation failed. Check headset tracking and retry.\n" +
                          "Thao tác anchor thất bại. Kiểm tra tracking của headset rồi thử lại.");
            }
            finally
            {
                _busy = false;
            }
        }

        private void BeginPlacement(string message)
        {
            _setupActive = true;
            _busy = false;
            _triggerWasPressed = true;
            if (_posePreview != null) _posePreview.SetActive(false);
            SetStatus(message);
            PlacementStarted?.Invoke();
        }

        private async Task<bool> WaitUntilTrackedAsync(ARAnchor anchor)
        {
            float elapsed = 0f;
            while (anchor != null && anchor.trackingState != TrackingState.Tracking &&
                   elapsed < AnchorTrackingTimeoutSeconds)
            {
                await Awaitable.NextFrameAsync();
                elapsed += Time.unscaledDeltaTime;
            }

            return anchor != null && anchor.trackingState == TrackingState.Tracking;
        }

        private void EnsureAnchorComponents()
        {
            GameObject originObject = _xrOrigin.gameObject;
            _arSession = FindAnyObjectByType<ARSession>();
            if (_arSession == null)
                _arSession = originObject.AddComponent<ARSession>();
            else
                _arSession.enabled = true;

            _anchorManager = _xrOrigin.GetComponent<ARAnchorManager>();
            if (_anchorManager == null)
                _anchorManager = originObject.AddComponent<ARAnchorManager>();
            else
                _anchorManager.enabled = true;
        }

        private static Transform[] FindContentRoots()
        {
            List<Transform> roots = new();
            for (int i = 0; i < ContentRootNames.Length; i++)
            {
                GameObject root = GameObject.Find(ContentRootNames[i]);
                if (root != null) roots.Add(root.transform);
            }

            return roots.ToArray();
        }

        private void StoreRootPoses(Transform anchorTransform)
        {
            List<StoredRootPose> poses = new(_contentRoots.Length);
            foreach (Transform root in _contentRoots)
            {
                if (root == null) continue;
                poses.Add(new StoredRootPose
                {
                    name = root.name,
                    localPosition = anchorTransform.InverseTransformPoint(root.position),
                    localRotation = Quaternion.Inverse(anchorTransform.rotation) * root.rotation
                });
            }

            PlayerPrefs.SetString(RelativePosesKey, JsonUtility.ToJson(new StoredRootPoseCollection { roots = poses.ToArray() }));
        }

        private void ApplySavedRootPoses(Transform anchorTransform)
        {
            string json = PlayerPrefs.GetString(RelativePosesKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            StoredRootPoseCollection collection = JsonUtility.FromJson<StoredRootPoseCollection>(json);
            if (collection?.roots == null) return;

            foreach (StoredRootPose pose in collection.roots)
            {
                Transform root = Array.Find(_contentRoots, candidate => candidate != null && candidate.name == pose.name);
                if (root == null) continue;
                root.SetPositionAndRotation(
                    anchorTransform.TransformPoint(pose.localPosition),
                    anchorTransform.rotation * pose.localRotation);
            }

            Physics.SyncTransforms();
            foreach (Unity.AI.Navigation.NavMeshSurface surface in FindObjectsByType<Unity.AI.Navigation.NavMeshSurface>(FindObjectsSortMode.None))
            {
                surface.RemoveData();
                surface.AddData();
            }
        }

        private bool TryGetRightControllerWorldPose(InputDevice device, out Pose worldPose)
        {
            worldPose = default;
            if (!device.isValid || _xrOrigin.TrackablesParent == null ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPosition) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion localRotation))
                return false;

            worldPose = _xrOrigin.TrackablesParent.TransformPose(new Pose(localPosition, localRotation));
            return true;
        }

        private void CreateStatusOverlay()
        {
            if (!_showSetupOverlay || Camera.main == null) return;

            GameObject canvasObject = new("Spatial Anchor Status");
            canvasObject.transform.SetParent(Camera.main.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, -0.22f, 1.15f);
            canvasObject.transform.localRotation = Quaternion.identity;

            _statusCanvas = canvasObject.AddComponent<Canvas>();
            _statusCanvas.renderMode = RenderMode.WorldSpace;
            _statusCanvas.sortingOrder = 250;
            RectTransform canvasRect = _statusCanvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(860f, 240f);
            canvasRect.localScale = Vector3.one * 0.001f;

            Image background = canvasObject.AddComponent<Image>();
            background.color = new Color(0.02f, 0.06f, 0.1f, 0.9f);

            GameObject textObject = new("Instructions", typeof(RectTransform));
            textObject.transform.SetParent(canvasObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 18f);
            textRect.offsetMax = new Vector2(-24f, -18f);
            _statusText = textObject.AddComponent<TextMeshProUGUI>();
            _statusText.font = TMP_Settings.defaultFontAsset;
            _statusText.fontSize = 30f;
            _statusText.enableAutoSizing = true;
            _statusText.fontSizeMin = 20f;
            _statusText.fontSizeMax = 30f;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.color = Color.white;
            _statusText.text = string.Empty;
        }

        private void CreatePosePreview()
        {
            _posePreview = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _posePreview.name = "Spatial Anchor Placement Preview";
            _posePreview.transform.localScale = Vector3.one * 0.035f;
            Collider collider = _posePreview.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = _posePreview.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = new Color(0.1f, 0.85f, 1f, 1f);

            GameObject lineObject = new("Anchor Forward Preview");
            lineObject.transform.SetParent(_posePreview.transform, false);
            _posePreviewLine = lineObject.AddComponent<LineRenderer>();
            _posePreviewLine.positionCount = 2;
            _posePreviewLine.startWidth = 0.012f;
            _posePreviewLine.endWidth = 0.004f;
            _posePreviewLine.material = renderer != null ? renderer.material : null;
            _posePreviewLine.enabled = false;
            _posePreview.SetActive(false);
        }

        private void UpdatePosePreview(Pose pose)
        {
            _posePreview.SetActive(true);
            _posePreview.transform.position = pose.position;
            _posePreviewLine.enabled = true;
            _posePreviewLine.SetPosition(0, pose.position);
            _posePreviewLine.SetPosition(1, pose.position + pose.rotation * Vector3.forward * 0.5f);
        }

        private void SetStatus(string message)
        {
            if (_statusText == null) CreateStatusOverlay();
            if (_statusText != null)
            {
                _statusText.text = message;
                _statusText.gameObject.SetActive(true);
            }
        }

        private void HideStatus()
        {
            if (_statusCanvas != null) _statusCanvas.gameObject.SetActive(false);
        }

        [Serializable]
        private sealed class StoredRootPoseCollection
        {
            public StoredRootPose[] roots;
        }

        [Serializable]
        private struct StoredRootPose
        {
            public string name;
            public Vector3 localPosition;
            public Quaternion localRotation;
        }
    }
}
