using ElectricPalletStackers.Ble;
using ElectricPalletStackers.Audio;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.PalletStackers;
using ElectricPalletStackers.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ElectricPalletStackers.Editor
{
    public static class PhoneCallUiBuilder
    {
        private const string PrefabPath = "Assets/_PalletStacker/UI/Prefabs/Phone Call Panel.prefab";
        private const string MainScenePath = "Assets/Scenes/MainScene.unity";

        private static readonly Color FacebookBlue = new(0.094f, 0.467f, 0.949f, 1f);
        private static readonly Color RejectRed = new(0.92f, 0.19f, 0.22f, 1f);
        private static readonly Color AcceptGreen = new(0.12f, 0.76f, 0.36f, 1f);

        [MenuItem("Tools/Electric Pallet Stacker/Build Phone Call Event")]
        public static void BuildFromMenu()
        {
            if (SceneManager.GetActiveScene().path != MainScenePath)
            {
                EditorUtility.DisplayDialog(
                    "Phone Call Event",
                    "Open Assets/Scenes/MainScene.unity before running this builder.",
                    "OK");
                return;
            }

            GameObject prefab = BuildPrefab();
            InstallInActiveScene(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Phone call event UI built and installed in MainScene.");
        }

        private static GameObject BuildPrefab()
        {
            GameObject root = new("Phone Call Panel", typeof(Rigidbody),
                typeof(BoxCollider), typeof(XRGrabInteractable), typeof(WorldSpacePanelGrab),
                typeof(PhoneCallPanel),
                typeof(PhoneCallAudioPresenter));

            GameObject canvasObject = new("Phone Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster), typeof(CanvasGroup));
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.SetParent(root.transform, false);
            canvasRect.sizeDelta = new Vector2(160f, 320f);
            canvasRect.localScale = Vector3.one * 0.001f;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.referencePixelsPerUnit = 100f;
            scaler.dynamicPixelsPerUnit = 10f;

            CanvasGroup canvasGroup = canvasObject.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            Rigidbody body = root.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            body.mass = 1f;
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            BoxCollider moveCollider = root.GetComponent<BoxCollider>();
            moveCollider.center = new Vector3(0f, -0.176f, 0f);
            moveCollider.size = new Vector3(0.14f, 0.045f, 0.025f);
            moveCollider.isTrigger = false;

            XRGrabInteractable grabInteractable = root.GetComponent<XRGrabInteractable>();
            grabInteractable.colliders.Clear();
            grabInteractable.colliders.Add(moveCollider);
            grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grabInteractable.useDynamicAttach = true;
            grabInteractable.trackPosition = true;
            grabInteractable.smoothPosition = false;
            grabInteractable.trackRotation = false;
            grabInteractable.trackScale = false;
            grabInteractable.throwOnDetach = false;
            grabInteractable.retainTransformParent = true;

            Sprite roundedSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite circleSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            Image background = CreateImage("Background", canvasRect, FacebookBlue, roundedSprite);
            Stretch(background.rectTransform, Vector2.zero, Vector2.zero);
            background.type = Image.Type.Sliced;
            background.raycastTarget = false;

            Shadow shadow = background.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(3f, -4f);

            Image moveHandle = CreateImage(
                "Move Handle", canvasRect, new Color(1f, 1f, 1f, 0.75f), roundedSprite);
            SetRect(moveHandle.rectTransform, new Vector2(0f, -176f), new Vector2(96f, 14f));
            moveHandle.type = Image.Type.Sliced;
            moveHandle.raycastTarget = false;

            TextMeshProUGUI status = CreateText(
                "Call Status", background.rectTransform, "Incoming call", 13f,
                FontStyles.Bold, new Color(1f, 1f, 1f, 0.82f));
            SetRect(status.rectTransform, new Vector2(0f, 118f), new Vector2(146f, 28f));
            EnableAutoSizing(status, 9f, 13f);

            Image avatar = CreateImage(
                "Caller Avatar", background.rectTransform, new Color(1f, 1f, 1f, 0.2f), circleSprite);
            SetRect(avatar.rectTransform, new Vector2(0f, 58f), new Vector2(68f, 68f));
            avatar.raycastTarget = false;

            TextMeshProUGUI avatarLabel = CreateText(
                "Avatar Initial", avatar.rectTransform, "?", 34f, FontStyles.Bold, Color.white);
            Stretch(avatarLabel.rectTransform, Vector2.zero, Vector2.zero);

            TextMeshProUGUI caller = CreateText(
                "Caller Number", background.rectTransform, "0123456789", 22f,
                FontStyles.Bold, Color.white);
            SetRect(caller.rectTransform, new Vector2(0f, 3f), new Vector2(145f, 38f));
            EnableAutoSizing(caller, 14f, 22f);

            TextMeshProUGUI hint = CreateText(
                "Interaction Hint", background.rectTransform, "Tap to answer", 11f,
                FontStyles.Normal, new Color(1f, 1f, 1f, 0.72f));
            SetRect(hint.rectTransform, new Vector2(0f, -29f), new Vector2(140f, 24f));

            RectTransform incomingActions = CreateContainer("Incoming Call Actions", background.rectTransform);
            Stretch(incomingActions, Vector2.zero, Vector2.zero);

            Button reject = CreateRoundButton(
                "Reject Button", incomingActions, new Vector2(-44f, -93f),
                RejectRed, circleSprite, roundedSprite);
            Button accept = CreateRoundButton(
                "Accept Button", incomingActions, new Vector2(44f, -93f),
                AcceptGreen, circleSprite, roundedSprite);

            TextMeshProUGUI rejectLabel = CreateText(
                "Reject Label", incomingActions, "Decline", 10f,
                FontStyles.Normal, new Color(1f, 1f, 1f, 0.9f));
            SetRect(rejectLabel.rectTransform, new Vector2(-44f, -137f), new Vector2(70f, 20f));

            TextMeshProUGUI acceptLabel = CreateText(
                "Accept Label", incomingActions, "Accept", 10f,
                FontStyles.Normal, new Color(1f, 1f, 1f, 0.9f));
            SetRect(acceptLabel.rectTransform, new Vector2(44f, -137f), new Vector2(70f, 20f));

            RectTransform activeCallActions = CreateContainer("Active Call Actions", background.rectTransform);
            Stretch(activeCallActions, Vector2.zero, Vector2.zero);
            Button hangUp = CreateRoundButton(
                "Hang Up Button", activeCallActions, new Vector2(0f, -93f),
                RejectRed, circleSprite, roundedSprite);
            TextMeshProUGUI hangUpLabel = CreateText(
                "Hang Up Label", activeCallActions, "End", 10f,
                FontStyles.Normal, new Color(1f, 1f, 1f, 0.9f));
            SetRect(hangUpLabel.rectTransform, new Vector2(0f, -137f), new Vector2(80f, 20f));
            activeCallActions.gameObject.SetActive(false);

            WorldSpacePanelGrab panelGrab = root.GetComponent<WorldSpacePanelGrab>();
            SerializedObject serializedPanelGrab = new(panelGrab);
            serializedPanelGrab.FindProperty("_grabCollider").objectReferenceValue = moveCollider;
            serializedPanelGrab.FindProperty("_grabInteractable").objectReferenceValue = grabInteractable;
            serializedPanelGrab.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject panel = new(root.GetComponent<PhoneCallPanel>());
            panel.FindProperty("_canvasGroup").objectReferenceValue = canvasGroup;
            panel.FindProperty("_content").objectReferenceValue = canvasRect;
            panel.FindProperty("_statusLabel").objectReferenceValue = status;
            panel.FindProperty("_callerLabel").objectReferenceValue = caller;
            panel.FindProperty("_hintLabel").objectReferenceValue = hint;
            panel.FindProperty("_incomingActions").objectReferenceValue = incomingActions.gameObject;
            panel.FindProperty("_activeCallActions").objectReferenceValue = activeCallActions.gameObject;
            panel.FindProperty("_rejectButton").objectReferenceValue = reject;
            panel.FindProperty("_acceptButton").objectReferenceValue = accept;
            panel.FindProperty("_hangUpButton").objectReferenceValue = hangUp;
            panel.FindProperty("_panelGrab").objectReferenceValue = panelGrab;
            panel.FindProperty("_moveCollider").objectReferenceValue = moveCollider;
            panel.FindProperty("_defaultCaller").stringValue = "0123456789";
            panel.ApplyModifiedPropertiesWithoutUndo();

            root.layer = LayerMask.NameToLayer("Default");
            SetLayerRecursively(canvasObject, LayerMask.NameToLayer("UI"));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void InstallInActiveScene(GameObject prefab)
        {
            PhoneCallEventSimulator existingSimulator =
                Object.FindFirstObjectByType<PhoneCallEventSimulator>(FindObjectsInactive.Include);
            if (existingSimulator != null) Object.DestroyImmediate(existingSimulator.gameObject);

            PhoneCallPanel existingPanel =
                Object.FindFirstObjectByType<PhoneCallPanel>(FindObjectsInactive.Include);
            if (existingPanel != null) Object.DestroyImmediate(existingPanel.gameObject);

            Transform uiParent = GameObject.Find("UIs")?.transform;
            GameObject panelObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            panelObject.name = "Phone Call Panel";
            if (uiParent != null) panelObject.transform.SetParent(uiParent, true);

            GameObject eventObject = new("Phone Call Event Simulator", typeof(PhoneCallEventSimulator));
            GameObject gameplay = GameObject.Find("Gameplay");
            if (gameplay != null) eventObject.transform.SetParent(gameplay.transform, false);

            AppManager appManager = Object.FindFirstObjectByType<AppManager>(FindObjectsInactive.Include);
            PalletStackerRigidbodyMotor vehicleMotor =
                Object.FindFirstObjectByType<PalletStackerRigidbodyMotor>(FindObjectsInactive.Include);
            PalletStackerCollisionReporter collisionReporter =
                Object.FindFirstObjectByType<PalletStackerCollisionReporter>(FindObjectsInactive.Include);
            PhoneCallPanel phonePanel = panelObject.GetComponent<PhoneCallPanel>();
            SerializedObject simulator = new(eventObject.GetComponent<PhoneCallEventSimulator>());
            simulator.FindProperty("_appManager").objectReferenceValue = appManager;
            simulator.FindProperty("_phoneCallPanel").objectReferenceValue = phonePanel;
            simulator.FindProperty("_vehicleMotor").objectReferenceValue = vehicleMotor;
            simulator.FindProperty("_vehicleBody").objectReferenceValue =
                vehicleMotor != null ? vehicleMotor.GetComponent<Rigidbody>() : null;
            simulator.FindProperty("_collisionReporter").objectReferenceValue = collisionReporter;
            simulator.FindProperty("_callerDisplay").stringValue = "0123456789";
            simulator.FindProperty("_initialDelayRange").vector2Value = new Vector2(5f, 10f);
            simulator.FindProperty("_ringDurationRange").vector2Value = new Vector2(10f, 15f);
            simulator.FindProperty("_conversationDurationRange").vector2Value = new Vector2(5f, 10f);
            simulator.FindProperty("_rejectRetryDelay").floatValue = 5f;
            simulator.ApplyModifiedPropertiesWithoutUndo();

            ConfigureHandGrabInteractors();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Selection.activeGameObject = eventObject;
        }

        private static void ConfigureHandGrabInteractors()
        {
            NearFarInteractor[] interactors = Object.FindObjectsByType<NearFarInteractor>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (NearFarInteractor interactor in interactors)
            {
                Transform hand = interactor != null ? interactor.transform.parent : null;
                if (hand == null || !hand.name.EndsWith("Hand", System.StringComparison.Ordinal)) continue;

                Transform aimPose = hand.Find("Aim Pose");
                if (aimPose == null) continue;

                interactor.attachTransform = aimPose;

                SphereInteractionCaster sphereCaster = interactor.GetComponent<SphereInteractionCaster>();
                if (sphereCaster != null) sphereCaster.castOrigin = aimPose;

                CurveInteractionCaster curveCaster = interactor.GetComponent<CurveInteractionCaster>();
                if (curveCaster != null) curveCaster.castOrigin = aimPose;

                InteractionAttachController attachController =
                    interactor.GetComponent<InteractionAttachController>();
                if (attachController != null) attachController.transformToFollow = aimPose;

                EditorUtility.SetDirty(interactor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(interactor);
                if (sphereCaster != null)
                {
                    EditorUtility.SetDirty(sphereCaster);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(sphereCaster);
                }

                if (curveCaster != null)
                {
                    EditorUtility.SetDirty(curveCaster);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(curveCaster);
                }

                if (attachController != null)
                {
                    EditorUtility.SetDirty(attachController);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(attachController);
                }
            }
        }

        private static Button CreateRoundButton(
            string name,
            RectTransform parent,
            Vector2 position,
            Color color,
            Sprite backgroundSprite,
            Sprite iconPlaceholderSprite)
        {
            Image image = CreateImage(name, parent, color, backgroundSprite);
            SetRect(image.rectTransform, position, new Vector2(68f, 68f));
            image.raycastTarget = true;

            Shadow shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.28f);
            shadow.effectDistance = new Vector2(2f, -3f);

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            Image icon = CreateImage(
                "Icon Placeholder", image.rectTransform, Color.white, iconPlaceholderSprite);
            SetRect(icon.rectTransform, Vector2.zero, new Vector2(24f, 24f));
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            return button;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color, Sprite sprite)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            return image;
        }

        private static RectTransform CreateContainer(string name, RectTransform parent)
        {
            GameObject gameObject = new(name, typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static TextMeshProUGUI CreateText(
            string name,
            RectTransform parent,
            string text,
            float fontSize,
            FontStyles style,
            Color color)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            TextMeshProUGUI label = gameObject.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
            return label;
        }

        private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void EnableAutoSizing(TextMeshProUGUI label, float minimumSize, float maximumSize)
        {
            label.enableAutoSizing = true;
            label.fontSizeMin = minimumSize;
            label.fontSizeMax = maximumSize;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void Stretch(RectTransform rect, Vector2 minOffset, Vector2 maxOffset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = minOffset;
            rect.offsetMax = maxOffset;
        }

        private static void SetLayerRecursively(GameObject gameObject, int layer)
        {
            gameObject.layer = layer;
            foreach (Transform child in gameObject.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
