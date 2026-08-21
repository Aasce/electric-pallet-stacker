#if UNITY_EDITOR
using ElectricPalletStackers.Ble;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ElectricPalletStackers.Ble.Editor
{
    [InitializeOnLoad]
    public static class FireVrBleSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/MainScene.unity";
        private const string RootName = "FIRE VR BLE Test";

        private static readonly Color BackgroundColor = new Color(0.035f, 0.055f, 0.09f, 0.97f);
        private static readonly Color SectionColor = new Color(0.075f, 0.105f, 0.16f, 0.98f);
        private static readonly Color PrimaryColor = new Color(0.08f, 0.48f, 0.78f, 1f);
        private static readonly Color SuccessColor = new Color(0.08f, 0.58f, 0.33f, 1f);
        private static readonly Color DangerColor = new Color(0.72f, 0.2f, 0.2f, 1f);
        private static readonly Color TextColor = new Color(0.9f, 0.94f, 1f, 1f);

        static FireVrBleSceneSetup()
        {
            EditorApplication.delayCall += CreateAutomaticallyIfMissing;
        }

        [MenuItem("Tools/FIRE VR/Create or Refresh BLE Test UI")]
        public static void CreateOrRefresh()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                Debug.LogError($"[FIRE VR] Open {ScenePath} before creating the BLE test UI.");
                return;
            }

            RemoveExistingBleTestObjects();

            GameObject root = new GameObject(RootName);
            FireVrBleTest bleTest = root.AddComponent<FireVrBleTest>();

            GameObject canvasObject = new GameObject(
                "FIRE VR BLE Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(TrackedDeviceGraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            scaler.referencePixelsPerUnit = 100f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1100f, 960f);
            canvasRect.position = new Vector3(0f, 1.6f, 2.1f);
            canvasRect.rotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.0012f;

            Image background = CreateImage("Background", canvasRect, BackgroundColor);
            Stretch(background.rectTransform, 0f);

            TMP_Text title = CreateText("Title", background.rectTransform, "FIRE VR - BLE CONNECTION TEST", 38f,
                FontStyles.Bold, TextAlignmentOptions.Center, TextColor);
            SetTopLeft(title.rectTransform, 20f, 18f, 1060f, 58f);

            TMP_Text status = CreateSectionText("Connection Status", background.rectTransform, 25f);
            SetTopLeft(status.rectTransform, 20f, 90f, 520f, 125f);

            TMP_Text gatt = CreateSectionText("GATT Status", background.rectTransform, 25f);
            SetTopLeft(gatt.rectTransform, 560f, 90f, 520f, 125f);

            CreateActionRow(background.rectTransform, bleTest, 235f);
            CreateCommandRow(background.rectTransform, bleTest, 320f);

            TMP_InputField input = CreateInputField(background.rectTransform);
            SetTopLeft(input.GetComponent<RectTransform>(), 20f, 405f, 850f, 65f);

            Button sendButton = CreateButton("Send Custom", background.rectTransform, "SEND FIRST CHARACTER", PrimaryColor);
            SetTopLeft(sendButton.GetComponent<RectTransform>(), 890f, 405f, 190f, 65f);
            UnityEventTools.AddPersistentListener(sendButton.onClick, bleTest.SendCommandFromInput);

            TMP_Text receive = CreateSectionText("Receive Data", background.rectTransform, 25f);
            SetTopLeft(receive.rectTransform, 20f, 490f, 1060f, 110f);

            Image logBackground = CreateImage("BLE Log", background.rectTransform, SectionColor);
            SetTopLeft(logBackground.rectTransform, 20f, 620f, 1060f, 255f);

            TMP_Text log = CreateText("Log Text", logBackground.rectTransform, "Waiting for BLE activity...", 20f,
                FontStyles.Normal, TextAlignmentOptions.TopLeft, TextColor);
            Stretch(log.rectTransform, 16f);
            log.textWrappingMode = TextWrappingModes.Normal;
            log.overflowMode = TextOverflowModes.Truncate;

            Button clearButton = CreateButton("Clear Log", background.rectTransform, "CLEAR LOG", new Color(0.28f, 0.32f, 0.4f, 1f));
            SetTopLeft(clearButton.GetComponent<RectTransform>(), 880f, 892f, 200f, 50f);
            UnityEventTools.AddPersistentListener(clearButton.onClick, bleTest.ClearUiLog);

            TMP_Text hint = CreateText("Hint", background.rectTransform,
                "ESP32 -> Unity: Notify A/B/C     |     Unity -> ESP32: Write H/S/R without response",
                19f, FontStyles.Italic, TextAlignmentOptions.Left, new Color(0.62f, 0.72f, 0.86f));
            SetTopLeft(hint.rectTransform, 20f, 900f, 840f, 42f);

            SerializedObject serializedTest = new SerializedObject(bleTest);
            SetObjectReference(serializedTest, "_statusText", status);
            SetObjectReference(serializedTest, "_gattText", gatt);
            SetObjectReference(serializedTest, "_receiveText", receive);
            SetObjectReference(serializedTest, "_logText", log);
            SetObjectReference(serializedTest, "_commandInput", input);
            SetBoolean(serializedTest, "_initializeOnStart", true);
            SetBoolean(serializedTest, "_scanAfterInitialize", true);
            SetBoolean(serializedTest, "_connectWhenFound", true);
            SetBoolean(serializedTest, "_subscribeAfterGattVerification", true);
            SetBoolean(serializedTest, "_enableKeyboardTest", true);
            serializedTest.ApplyModifiedPropertiesWithoutUndo();

            bleTest.RefreshUi();
            EditorUtility.SetDirty(bleTest);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
            Debug.Log("[FIRE VR] BLE test component, UI fields, buttons, and MainScene wiring created successfully.");
        }

        private static void CreateAutomaticallyIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += CreateAutomaticallyIfMissing;
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            FireVrBleTest[] tests = Object.FindObjectsByType<FireVrBleTest>(FindObjectsSortMode.None);
            bool setupMissingOrDuplicated = GameObject.Find(RootName) == null || tests.Length != 1;
            bool uiFieldsMissing = tests.Length == 1 && !AreUiFieldsAssigned(tests[0]);
            if (scene.IsValid() && scene.path == ScenePath && (setupMissingOrDuplicated || uiFieldsMissing))
                CreateOrRefresh();
        }

        private static bool AreUiFieldsAssigned(FireVrBleTest test)
        {
            SerializedObject serializedTest = new SerializedObject(test);
            return HasObjectReference(serializedTest, "_statusText")
                   && HasObjectReference(serializedTest, "_gattText")
                   && HasObjectReference(serializedTest, "_receiveText")
                   && HasObjectReference(serializedTest, "_logText")
                   && HasObjectReference(serializedTest, "_commandInput");
        }

        private static bool HasObjectReference(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            return property != null && property.objectReferenceValue != null;
        }

        private static void RemoveExistingBleTestObjects()
        {
            FireVrBleTest[] existingTests = Object.FindObjectsByType<FireVrBleTest>(FindObjectsSortMode.None);
            foreach (FireVrBleTest existingTest in existingTests)
            {
                GameObject gameObject = existingTest.gameObject;
                bool dedicatedObject = gameObject.GetComponents<Component>().Length == 2;
                if (dedicatedObject)
                    Object.DestroyImmediate(gameObject);
                else
                    Object.DestroyImmediate(existingTest);
            }

            GameObject existingRoot = GameObject.Find(RootName);
            if (existingRoot != null)
                Object.DestroyImmediate(existingRoot);
        }

        private static void CreateActionRow(RectTransform parent, FireVrBleTest bleTest, float y)
        {
            const float gap = 12f;
            const float width = 202.4f;
            string[] labels = { "INITIALIZE", "SCAN", "CONNECT", "SUBSCRIBE TX", "DISCONNECT" };
            Color[] colors = { PrimaryColor, PrimaryColor, PrimaryColor, SuccessColor, DangerColor };

            for (int index = 0; index < labels.Length; index++)
            {
                Button button = CreateButton(labels[index], parent, labels[index], colors[index]);
                SetTopLeft(button.GetComponent<RectTransform>(), 20f + index * (width + gap), y, width, 65f);
                switch (index)
                {
                    case 0: UnityEventTools.AddPersistentListener(button.onClick, bleTest.InitializeBle); break;
                    case 1: UnityEventTools.AddPersistentListener(button.onClick, bleTest.StartScan); break;
                    case 2: UnityEventTools.AddPersistentListener(button.onClick, bleTest.Connect); break;
                    case 3: UnityEventTools.AddPersistentListener(button.onClick, bleTest.SubscribeTX); break;
                    case 4: UnityEventTools.AddPersistentListener(button.onClick, bleTest.Disconnect); break;
                }
            }
        }

        private static void CreateCommandRow(RectTransform parent, FireVrBleTest bleTest, float y)
        {
            string[] names = { "Send H", "Send S", "Send R" };
            string[] labels = { "H - WALL HIT", "S - REQUEST STATE", "R - TEST COMMAND" };
            string[] commands = { "H", "S", "R" };

            for (int index = 0; index < names.Length; index++)
            {
                Button button = CreateButton(names[index], parent, labels[index], new Color(0.16f, 0.38f, 0.58f, 1f));
                SetTopLeft(button.GetComponent<RectTransform>(), 20f + index * 360f, y, 340f, 65f);
                UnityEventTools.AddStringPersistentListener(button.onClick, bleTest.SendCommand, commands[index]);
            }
        }

        private static TMP_InputField CreateInputField(RectTransform parent)
        {
            GameObject fieldObject = new GameObject("Command Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            fieldObject.transform.SetParent(parent, false);
            Image image = fieldObject.GetComponent<Image>();
            image.color = SectionColor;

            GameObject viewportObject = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(fieldObject.transform, false);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            Stretch(viewport, 14f);

            TMP_Text text = CreateText("Text", viewport, string.Empty, 26f, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft, TextColor);
            Stretch(text.rectTransform, 0f);

            TMP_Text placeholder = CreateText("Placeholder", viewport, "Command (H / S / R / first character)", 25f,
                FontStyles.Italic, TextAlignmentOptions.MidlineLeft, new Color(0.48f, 0.56f, 0.68f, 1f));
            Stretch(placeholder.rectTransform, 0f);

            TMP_InputField input = fieldObject.GetComponent<TMP_InputField>();
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 16;
            input.caretColor = TextColor;
            input.selectionColor = new Color(0.1f, 0.5f, 0.85f, 0.5f);
            return input;
        }

        private static Button CreateButton(string name, RectTransform parent, string label, Color color)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = color;

            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colorBlock = button.colors;
            colorBlock.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colorBlock.pressedColor = Color.Lerp(color, Color.black, 0.2f);
            colorBlock.selectedColor = colorBlock.highlightedColor;
            colorBlock.disabledColor = new Color(color.r, color.g, color.b, 0.35f);
            button.colors = colorBlock;

            TMP_Text text = CreateText("Label", buttonObject.GetComponent<RectTransform>(), label, 23f,
                FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            Stretch(text.rectTransform, 8f);
            return button;
        }

        private static TMP_Text CreateSectionText(string name, RectTransform parent, float fontSize)
        {
            Image image = CreateImage(name, parent, SectionColor);
            TMP_Text text = CreateText("Text", image.rectTransform, name, fontSize, FontStyles.Normal,
                TextAlignmentOptions.TopLeft, TextColor);
            Stretch(text.rectTransform, 14f);
            return text;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static TMP_Text CreateText(string name, RectTransform parent, string value, float fontSize,
            FontStyles style, TextAlignmentOptions alignment, Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }

        private static void SetObjectReference(SerializedObject serializedObject, string propertyName, Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.objectReferenceValue = value;
        }

        private static void SetBoolean(SerializedObject serializedObject, string propertyName, bool value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.boolValue = value;
        }

        private static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
#endif
