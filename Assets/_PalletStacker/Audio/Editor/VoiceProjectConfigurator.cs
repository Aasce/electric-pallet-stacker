using System;
using System.Collections.Generic;
using System.IO;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.Localization;
using ElectricPalletStackers.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElectricPalletStackers.Audio.Editor
{
    public static class VoiceProjectConfigurator
    {
        private const string InventoryPath =
            "Assets/_PalletStacker/Audio/voice_script_inventory.json";
        private const string VoiceLibraryPath =
            "Assets/_PalletStacker/Audio/Resources/PalletStackerVoiceLibrary.asset";
        private const string PlaceholderPath =
            "Assets/_PalletStacker/Audio/Clips/Phone/Lorem Ipsum Voice.wav";
        private const string AudioManagerPrefabPath =
            "Assets/_PalletStacker/Audio/Audio Manager.prefab";
        private const string PhonePanelPrefabPath =
            "Assets/_PalletStacker/UI/Prefabs/Phone Call Panel.prefab";
        private const string MainScenePath = "Assets/Scenes/MainScene.unity";

        [Serializable]
        private sealed class Inventory
        {
            public List<InventoryItem> items = new();
        }

        [Serializable]
        private sealed class InventoryItem
        {
            public string id;
            public string category;
            public string priority;
        }

        private readonly struct UIFlowBindingData
        {
            public UIFlowBindingData(UIFlowState state, string cueId)
            {
                State = state;
                CueId = cueId;
            }

            public UIFlowState State { get; }
            public string CueId { get; }
        }

        private readonly struct FailureBindingData
        {
            public FailureBindingData(GameFailureReason reason, string cueId)
            {
                Reason = reason;
                CueId = cueId;
            }

            public GameFailureReason Reason { get; }
            public string CueId { get; }
        }

        [MenuItem("Tools/Electric Pallet Stacker/Voice/Configure Project")]
        public static void ConfigureProject()
        {
            AssetDatabase.Refresh();
            Inventory inventory = LoadInventory();
            VoiceClipLibrary library = BuildVoiceLibrary(inventory);
            ConfigureAudioManagerPrefab(library);
            ConfigurePhonePanelPrefab();
            ConfigureMainScene();
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[Voice] Configured {inventory.items.Count} localized cues, " +
                "AudioManager, and situation presenters.");
        }

        private static Inventory LoadInventory()
        {
            if (!File.Exists(InventoryPath))
                throw new FileNotFoundException("Voice inventory was not found.", InventoryPath);

            Inventory inventory = JsonUtility.FromJson<Inventory>(File.ReadAllText(InventoryPath));
            if (inventory?.items == null || inventory.items.Count == 0)
                throw new InvalidDataException("Voice inventory contains no items.");
            return inventory;
        }

        private static VoiceClipLibrary BuildVoiceLibrary(Inventory inventory)
        {
            VoiceClipLibrary library = AssetDatabase.LoadAssetAtPath<VoiceClipLibrary>(
                VoiceLibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<VoiceClipLibrary>();
                AssetDatabase.CreateAsset(library, VoiceLibraryPath);
            }

            AudioClip placeholder = AssetDatabase.LoadAssetAtPath<AudioClip>(PlaceholderPath);
            if (placeholder == null)
                throw new InvalidDataException($"Voice placeholder is missing: {PlaceholderPath}");

            SerializedObject serializedLibrary = new(library);
            SerializedProperty entries = serializedLibrary.FindProperty("_entries");
            entries.ClearArray();

            for (int index = 0; index < inventory.items.Count; index++)
            {
                InventoryItem item = inventory.items[index];
                if (item == null || string.IsNullOrWhiteSpace(item.id)) continue;

                int entryIndex = entries.arraySize;
                entries.InsertArrayElementAtIndex(entryIndex);
                SerializedProperty entry = entries.GetArrayElementAtIndex(entryIndex);
                entry.FindPropertyRelative("_id").stringValue = item.id.Trim();
                entry.FindPropertyRelative("_category").stringValue = item.category ?? string.Empty;
                entry.FindPropertyRelative("_priority").stringValue = item.priority ?? string.Empty;
                AssignClip(entry, "_vietnamese", item.id, "vi", placeholder);
                AssignClip(entry, "_english", item.id, "en", placeholder);
                AssignClip(entry, "_japanese", item.id, "jp", placeholder);
            }

            serializedLibrary.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);
            return library;
        }

        private static void AssignClip(
            SerializedProperty entry,
            string propertyName,
            string cueId,
            string language,
            AudioClip placeholder)
        {
            string path =
                $"Assets/_PalletStacker/Audio/Clips/Voice/{language}/{cueId}.{language}.wav";
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            entry.FindPropertyRelative(propertyName).objectReferenceValue = clip != null
                ? clip
                : placeholder;
        }

        private static void ConfigureAudioManagerPrefab(VoiceClipLibrary library)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AudioManagerPrefabPath);
            try
            {
                AudioManager manager = root.GetComponent<AudioManager>();
                if (manager == null)
                    throw new MissingComponentException("Audio Manager prefab has no AudioManager.");

                SerializedObject serializedManager = new(manager);
                SerializedProperty voice = serializedManager.FindProperty("_voice");
                voice.FindPropertyRelative("_library").objectReferenceValue = library;
                serializedManager.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, AudioManagerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigurePhonePanelPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PhonePanelPrefabPath);
            try
            {
                PhoneCallAudioPresenter presenter =
                    root.GetComponentInChildren<PhoneCallAudioPresenter>(true);
                if (presenter == null)
                    throw new MissingComponentException(
                        "Phone Call Panel prefab has no PhoneCallAudioPresenter.");

                SerializedObject serializedPresenter = new(presenter);
                SetCue(
                    serializedPresenter.FindProperty("_conversationVoice"),
                    "phone.conversation");
                serializedPresenter.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PhonePanelPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureMainScene()
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != MainScenePath)
                scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            UIController uiController = FindSceneObject<UIController>();
            AppManager appManager = FindSceneObject<AppManager>();

            if (uiController == null || appManager == null)
                throw new MissingReferenceException(
                    "MainScene requires UIController and AppManager.");

            ConfigureLanguageSync(uiController);
            ConfigureUIFlowPresenter(uiController);
            ConfigureFailurePresenter(appManager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureLanguageSync(UIController uiController)
        {
            VoiceLanguageSync sync = GetOrAdd<VoiceLanguageSync>(uiController.gameObject);
            LocalizationManager localization = uiController.GetComponent<LocalizationManager>();
            SerializedObject serializedSync = new(sync);
            serializedSync.FindProperty("_localizationManager").objectReferenceValue = localization;
            serializedSync.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureUIFlowPresenter(UIController uiController)
        {
            UIFlowVoicePresenter presenter = GetOrAdd<UIFlowVoicePresenter>(uiController.gameObject);
            SerializedObject serializedPresenter = new(presenter);
            serializedPresenter.FindProperty("_controller").objectReferenceValue = uiController;

            UIFlowBindingData[] bindings =
            {
                new(UIFlowState.CalibrateVehicleView, "calibration.description"),
                new(UIFlowState.Welcome, "welcome.introduction"),
                new(UIFlowState.PpeEquipment, "ppe.description"),
                new(UIFlowState.VehicleChecklist, "vehicleChecklist.instruction"),
                new(UIFlowState.Completed, "completed.experience")
            };

            SerializedProperty list = serializedPresenter.FindProperty("_bindings");
            list.arraySize = bindings.Length;
            for (int index = 0; index < bindings.Length; index++)
            {
                SerializedProperty binding = list.GetArrayElementAtIndex(index);
                binding.FindPropertyRelative("_state").intValue = (int)bindings[index].State;
                SetCue(binding.FindPropertyRelative("_cue"), bindings[index].CueId);
            }
            serializedPresenter.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureFailurePresenter(AppManager appManager)
        {
            GameFailureVoicePresenter presenter =
                GetOrAdd<GameFailureVoicePresenter>(appManager.gameObject);
            SerializedObject serializedPresenter = new(presenter);
            serializedPresenter.FindProperty("_appManager").objectReferenceValue = appManager;

            FailureBindingData[] bindings =
            {
                new(GameFailureReason.Unknown, "failure.unknown.result"),
                new(GameFailureReason.VehicleCollision, "failure.vehicleCollision.result"),
                new(GameFailureReason.OperatorCollision, "failure.operatorCollision.result"),
                new(GameFailureReason.LoadCollision, "failure.loadCollision.result"),
                new(GameFailureReason.LoadDropped, "failure.loadDropped.result"),
                new(GameFailureReason.PedestrianCollision, "failure.pedestrianCollision.result"),
                new(
                    GameFailureReason.PhoneDistractionCollision,
                    "failure.phoneDistractionCollision.result"),
                new(
                    GameFailureReason.IntersectionVehicleCollision,
                    "failure.intersectionVehicleCollision.result"),
                new(
                    GameFailureReason.OperatorFootRunOver,
                    "failure.operatorFootRunOver.result")
            };

            SerializedProperty list = serializedPresenter.FindProperty("_bindings");
            list.arraySize = bindings.Length;
            for (int index = 0; index < bindings.Length; index++)
            {
                SerializedProperty binding = list.GetArrayElementAtIndex(index);
                binding.FindPropertyRelative("_reason").intValue = (int)bindings[index].Reason;
                SetCue(binding.FindPropertyRelative("_cue"), bindings[index].CueId);
            }
            serializedPresenter.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetCue(SerializedProperty cueProperty, string id)
        {
            cueProperty.FindPropertyRelative("_id").stringValue = id;
        }

        private static T GetOrAdd<T>(GameObject owner) where T : Component
        {
            T component = owner.GetComponent<T>();
            return component != null ? component : owner.AddComponent<T>();
        }

        private static T FindSceneObject<T>() where T : UnityEngine.Object
        {
            T[] objects = UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            return objects.Length > 0 ? objects[0] : null;
        }
    }
}
