using System;
using System.Collections.Generic;
using System.Linq;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.NPCs;
using ElectricPalletStackers.PalletStackers;
using ElectricPalletStackers.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ElectricPalletStackers.Editor
{
    public static class TrainingExperienceSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/MainScene.unity";
        private const string NpcPrefabPath = "Assets/_PalletStacker/NPC/NPC.prefab";
        private const string PhonePrefabPath = "Assets/_PalletStacker/UI/Prefabs/Phone Call Panel.prefab";
        private const string MaterialFolder = "Assets/_PalletStacker/Environment/Materials";
        private const string RootName = "Training Experience Route";
        private const string TemplateRootName = "Template";

        [MenuItem("Tools/Electric Pallet Stacker/Rebuild Training Experience")]
        public static void BuildMainScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            StripNavigationFromScene(scene);
            StripNavigationFromNpcPrefab();
            RemovePhoneEndControls();

            GameObject gameplay = GameObject.Find("Gameplay") ?? new GameObject("Gameplay");
            GameObject environment = GameObject.Find("Enviroments") ?? new GameObject("Enviroments");
            Transform template = GameObject.Find(TemplateRootName)?.transform;
            if (template == null)
                throw new InvalidOperationException(
                    "MainScene requires the authoring GameObject 'Template'.");

            Vector3 startPosition = RequireTemplatePoint(template, "Start").position;
            Vector3 pickupPosition = RequireTemplatePoint(template, "Pickup Pallet").position;
            Vector3 cornerPosition = RequireTemplatePoint(template, "Turning point").position;
            Vector3 crossroadsPosition = RequireTemplatePoint(template, "Crossroads").position;
            Vector3 phonePosition = RequireTemplatePoint(template, "Phone calling").position;
            Vector3 deliveryTurnPosition = RequireTemplatePoint(template, "Turning point (1)").position;
            Vector3 destinationPosition = RequireTemplatePoint(template, "End game").position;

            Transform oldRoot = environment.transform.Find(RootName);
            if (oldRoot != null) UnityEngine.Object.DestroyImmediate(oldRoot.gameObject);

            GameObject routeRoot = new(RootName);
            routeRoot.transform.SetParent(environment.transform, false);
            TrainingExperienceController experience = routeRoot.AddComponent<TrainingExperienceController>();

            EnsureFolder(MaterialFolder);
            Material lineMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Safety Line.mat", new Color(1f, 0.72f, 0.05f));
            Material vehicleMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Crossing Vehicle.mat", new Color(0.12f, 0.42f, 0.88f));
            RemoveObsoleteRouteMaterials();

            Transform routeData = CreateGroup("Route Logic", routeRoot.transform).transform;
            CrossingVehicleController crossingVehicle = BuildCrossingVehicle(
                routeData, crossroadsPosition, vehicleMaterial, lineMaterial);

            PhoneCallEventSimulator phone = FindFirst<PhoneCallEventSimulator>();
            PalletRoundController palletRound = FindFirst<PalletRoundController>();
            PalletStackerLoadHandler loadHandler = FindFirst<PalletStackerLoadHandler>();
            ExperienceCheckpointTrigger[] checkpoints =
            {
                CreateCheckpoint("01 Corner Checkpoint", routeData, AtTriggerHeight(cornerPosition),
                    new Vector3(4f, 2f, 5f), ExperienceCheckpoint.Corner, experience),
                CreateCheckpoint("02 Yield Intersection", routeData, AtTriggerHeight(crossroadsPosition),
                    new Vector3(4f, 2f, 5f), ExperienceCheckpoint.YieldIntersection, experience),
                CreateCheckpoint("03 Phone Intersection", routeData, AtTriggerHeight(phonePosition),
                    new Vector3(4f, 2f, 5f), ExperienceCheckpoint.PhoneIntersection, experience)
            };

            ConfigureExperience(experience, palletRound, loadHandler, crossingVehicle, phone, checkpoints);
            ConfigureDeterministicPalletRoute(
                palletRound, startPosition, pickupPosition, destinationPosition);
            ConfigureWaypointPopulation(
                routeRoot.transform, crossroadsPosition, phonePosition,
                deliveryTurnPosition, destinationPosition);
            ConfigurePhoneEvent(phone);
            ConfigureAppManager(experience, phone);
            ConfigurePpePanel();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TrainingExperience] MainScene rebuilt from Template markers: warehouse paths preserved, waypoint NPCs, yield vehicle, phone event and PPE placeholder installed.");
        }

        private static CrossingVehicleController BuildCrossingVehicle(
            Transform parent,
            Vector3 crossroadsPosition,
            Material vehicleMaterial,
            Material accentMaterial)
        {
            Vector3 crossingCenter = Grounded(crossroadsPosition);
            Transform start = CreatePoint(
                "Crossing Vehicle Start", parent, crossingCenter + Vector3.back * 6f);
            Transform end = CreatePoint(
                "Crossing Vehicle End", parent, crossingCenter + Vector3.forward * 6f);
            GameObject root = new("Crossing Pallet Truck", typeof(BoxCollider), typeof(Rigidbody),
                typeof(CrossingVehicleController));
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(start.position, Quaternion.identity);
            BoxCollider collider = root.GetComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.7f, 0f);
            collider.size = new Vector3(1.5f, 1.4f, 2.7f);
            Rigidbody body = root.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            CreateLocalCube("Chassis", root.transform, new Vector3(0f, 0.45f, 0f),
                new Vector3(1.35f, 0.55f, 2.4f), vehicleMaterial);
            CreateLocalCube("Mast", root.transform, new Vector3(0f, 1.25f, 0.75f),
                new Vector3(1.15f, 1.7f, 0.22f), vehicleMaterial);
            CreateLocalCube("Fork Left", root.transform, new Vector3(-0.38f, 0.18f, 1.55f),
                new Vector3(0.2f, 0.12f, 1.7f), accentMaterial);
            CreateLocalCube("Fork Right", root.transform, new Vector3(0.38f, 0.18f, 1.55f),
                new Vector3(0.2f, 0.12f, 1.7f), accentMaterial);

            SerializedObject serialized = new(root.GetComponent<CrossingVehicleController>());
            serialized.FindProperty("_startPoint").objectReferenceValue = start;
            serialized.FindProperty("_endPoint").objectReferenceValue = end;
            serialized.FindProperty("_speed").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root.GetComponent<CrossingVehicleController>();
        }

        private static ExperienceCheckpointTrigger CreateCheckpoint(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            ExperienceCheckpoint checkpoint,
            TrainingExperienceController experience)
        {
            GameObject triggerObject = new(name, typeof(BoxCollider), typeof(ExperienceCheckpointTrigger));
            triggerObject.transform.SetParent(parent, false);
            triggerObject.transform.position = position;
            BoxCollider collider = triggerObject.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = size;
            SerializedObject serialized = new(triggerObject.GetComponent<ExperienceCheckpointTrigger>());
            serialized.FindProperty("_experience").objectReferenceValue = experience;
            serialized.FindProperty("_checkpoint").enumValueIndex = (int)checkpoint;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return triggerObject.GetComponent<ExperienceCheckpointTrigger>();
        }

        private static void ConfigureExperience(
            TrainingExperienceController experience,
            PalletRoundController palletRound,
            PalletStackerLoadHandler loadHandler,
            CrossingVehicleController crossingVehicle,
            PhoneCallEventSimulator phone,
            ExperienceCheckpointTrigger[] checkpoints)
        {
            SerializedObject serialized = new(experience);
            serialized.FindProperty("_palletRoundController").objectReferenceValue = palletRound;
            serialized.FindProperty("_loadHandler").objectReferenceValue = loadHandler;
            serialized.FindProperty("_crossingVehicle").objectReferenceValue = crossingVehicle;
            serialized.FindProperty("_phoneCallEvent").objectReferenceValue = phone;
            SetObjectArray(serialized.FindProperty("_checkpointTriggers"), checkpoints);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureDeterministicPalletRoute(
            PalletRoundController palletRound,
            Vector3 startPosition,
            Vector3 pickupPosition,
            Vector3 destinationPosition)
        {
            PalletSpawner spawner = FindFirst<PalletSpawner>();
            Transform spawn = GameObject.Find("Pallet Spawn 01")?.transform;
            if (spawn != null)
            {
                spawn.position = Grounded(pickupPosition);
                spawn.rotation = Quaternion.identity;
            }
            if (spawner != null)
            {
                SerializedObject serializedSpawner = new(spawner);
                SetObjectArray(serializedSpawner.FindProperty("_spawnPoints"), new[] { spawn });
                serializedSpawner.FindProperty("_randomizeSpawn").boolValue = false;
                serializedSpawner.ApplyModifiedPropertiesWithoutUndo();
            }

            PalletDestinationZone destination = FindObjects<PalletDestinationZone>()
                .FirstOrDefault(zone => zone.name.Contains("03", StringComparison.Ordinal));
            if (destination != null)
            {
                destination.transform.position = Grounded(destinationPosition);
                destination.transform.rotation = Quaternion.identity;
            }

            PalletStacker stacker = FindFirst<PalletStacker>();
            if (stacker != null)
            {
                stacker.transform.position = Grounded(startPosition);
                stacker.transform.rotation = Quaternion.identity;
            }

            PalletStackerLoad load = FindFirst<PalletStackerLoad>();
            if (load != null)
            {
                load.transform.position = Grounded(pickupPosition);
                load.transform.rotation = Quaternion.identity;
            }

            if (palletRound != null)
            {
                SerializedObject serializedRound = new(palletRound);
                SetObjectArray(serializedRound.FindProperty("_destinationZones"),
                    new[] { destination });
                serializedRound.FindProperty("_randomizeDestination").boolValue = false;
                serializedRound.FindProperty("_armDestinationImmediately").boolValue = false;
                serializedRound.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void ConfigureWaypointPopulation(
            Transform routeRoot,
            Vector3 crossroadsPosition,
            Vector3 phonePosition,
            Vector3 deliveryTurnPosition,
            Vector3 destinationPosition)
        {
            NpcPopulationController population = FindFirst<NpcPopulationController>();
            if (population == null) return;
            Transform routesRoot = CreateGroup("NPC Waypoint Routes", routeRoot).transform;
            NpcWaypointRoute[] routes =
            {
                CreateRoute("Phone Crossing A", routesRoot, new[]
                {
                    Offset(phonePosition, 0f, -4f), Offset(phonePosition, 0f, 4f),
                    Offset(phonePosition, 3f, 4f), Offset(phonePosition, 3f, -4f)
                }),
                CreateRoute("Phone Crossing B", routesRoot, new[]
                {
                    Offset(phonePosition, -3f, 4f), Offset(phonePosition, 2f, 4f),
                    Offset(phonePosition, 2f, -4f), Offset(phonePosition, -3f, -4f)
                }),
                CreateRoute("Yield Crossing", routesRoot, new[]
                {
                    Offset(crossroadsPosition, 0f, -4f), Offset(crossroadsPosition, 0f, 4f),
                    Offset(crossroadsPosition, -4f, 4f), Offset(crossroadsPosition, -4f, -4f)
                }),
                CreateRoute("Delivery Crossing", routesRoot, new[]
                {
                    Offset(deliveryTurnPosition, -4f, 3f), Offset(deliveryTurnPosition, 4f, 3f),
                    Offset(destinationPosition, 4f, -2f), Offset(destinationPosition, -4f, -2f)
                })
            };

            Transform runtimeParent = CreateGroup("Waypoint NPC Runtime", routeRoot).transform;
            PalletStackerRigidbodyMotor motor = FindFirst<PalletStackerRigidbodyMotor>();
            PalletStackerHornOutput horn = FindFirst<PalletStackerHornOutput>();
            Rigidbody vehicleBody = motor != null ? motor.GetComponent<Rigidbody>() : null;
            SerializedObject serialized = new(population);
            serialized.FindProperty("_normalNpcCount").intValue = 4;
            serialized.FindProperty("_busyNpcCount").intValue = 8;
            serialized.FindProperty("_runtimeParent").objectReferenceValue = runtimeParent;
            SetObjectArray(serialized.FindProperty("_routes"), routes);
            serialized.FindProperty("_vehicleTransform").objectReferenceValue =
                vehicleBody != null ? vehicleBody.transform : null;
            serialized.FindProperty("_vehicleBody").objectReferenceValue = vehicleBody;
            serialized.FindProperty("_hornOutput").objectReferenceValue = horn;
            serialized.FindProperty("_hornReactionRadius").floatValue = 8f;
            serialized.FindProperty("_hornMinimumStopDuration").floatValue = 2.5f;
            serialized.FindProperty("_busySpeedMultiplier").floatValue = 1.65f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static NpcWaypointRoute CreateRoute(string name, Transform parent, Vector3[] positions)
        {
            GameObject routeObject = new(name, typeof(NpcWaypointRoute));
            routeObject.transform.SetParent(parent, false);
            Transform[] points = new Transform[positions.Length];
            for (int index = 0; index < positions.Length; index++)
                points[index] = CreatePoint($"Waypoint {index + 1:00}", routeObject.transform, positions[index]);
            SerializedObject serialized = new(routeObject.GetComponent<NpcWaypointRoute>());
            SetObjectArray(serialized.FindProperty("_waypoints"), points);
            serialized.FindProperty("_loop").boolValue = true;
            serialized.FindProperty("_pingPong").boolValue = false;
            serialized.FindProperty("_waitAtWaypoint").vector2Value = new Vector2(0.5f, 1.25f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return routeObject.GetComponent<NpcWaypointRoute>();
        }

        private static void ConfigurePhoneEvent(PhoneCallEventSimulator phone)
        {
            if (phone == null) return;
            NpcPopulationController population = FindFirst<NpcPopulationController>();
            SerializedObject serialized = new(phone);
            serialized.FindProperty("_npcPopulation").objectReferenceValue = population;
            serialized.FindProperty("_conversationDuration").floatValue = 10f;
            serialized.FindProperty("_movingSpeedThreshold").floatValue = 0.1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureAppManager(
            TrainingExperienceController experience,
            PhoneCallEventSimulator phone)
        {
            AppManager app = FindFirst<AppManager>();
            if (app == null) return;
            SerializedObject serialized = new(app);
            SerializedProperty participants = serialized.FindProperty("_roundParticipantComponents");
            List<MonoBehaviour> participantValues = ReadObjectArray<MonoBehaviour>(participants);
            if (!participantValues.Contains(experience)) participantValues.Add(experience);
            SetObjectArray(participants, participantValues.ToArray());

            SetObjectArray(serialized.FindProperty("_victorySourceComponents"),
                new MonoBehaviour[] { experience });

            SerializedProperty resettables = serialized.FindProperty("_resettableComponents");
            List<MonoBehaviour> resettableValues = ReadObjectArray<MonoBehaviour>(resettables);
            if (!resettableValues.Contains(experience)) resettableValues.Add(experience);
            if (phone != null && !resettableValues.Contains(phone)) resettableValues.Add(phone);
            SetObjectArray(resettables, resettableValues.ToArray());
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigurePpePanel()
        {
            UIGuidePanel panel = FindFirst<UIGuidePanel>();
            if (panel == null) return;
            panel.gameObject.name = "PPE Preparation Panel";
            DisableNamedChild(panel.transform, "Scroll View");
            DisableNamedChild(panel.transform, "Scroll Hint");

            TMP_Text title = FindNamedComponent<TMP_Text>(panel.transform, "Title");
            if (title != null)
            {
                RemoveLocalization(title.gameObject);
                title.text = "PERSONAL PROTECTIVE EQUIPMENT";
            }
            TMP_Text hint = FindNamedComponent<TMP_Text>(panel.transform, "Confirm Hint");
            if (hint != null)
            {
                RemoveLocalization(hint.gameObject);
                hint.text = "POKE OK TO CONTINUE";
            }

            Transform existing = FindChildRecursive(panel.transform, "PPE OK Button");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            GameObject buttonObject = new("PPE OK Button", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(panel.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -150f);
            rect.sizeDelta = new Vector2(240f, 84f);
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.02f, 0.63f, 0.84f, 1f);

            GameObject labelObject = new("Label", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "OK";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 34f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;

            SerializedObject serialized = new(panel);
            serialized.FindProperty("_okButton").objectReferenceValue = buttonObject.GetComponent<Button>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void StripNavigationFromScene(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
                Component[] components = transform.GetComponents<Component>();
                foreach (Component component in components)
                {
                    if (component == null) continue;
                    string typeName = component.GetType().FullName ?? string.Empty;
                    if (typeName.Contains("NavMeshAgent", StringComparison.Ordinal) ||
                        typeName.Contains("NavMeshSurface", StringComparison.Ordinal) ||
                        typeName.Contains("NavMeshModifier", StringComparison.Ordinal) ||
                        typeName.Contains("NavMeshLink", StringComparison.Ordinal) ||
                        typeName.Contains("NavMeshObstacle", StringComparison.Ordinal))
                        UnityEngine.Object.DestroyImmediate(component);
                }
            }
        }

        private static void StripNavigationFromNpcPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(NpcPrefabPath);
            try
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    Component[] components = transform.GetComponents<Component>();
                    foreach (Component component in components)
                    {
                        if (component == null) continue;
                        string typeName = component.GetType().FullName ?? string.Empty;
                        if (typeName.Contains("NavMeshAgent", StringComparison.Ordinal) ||
                            typeName.Contains("NavMeshObstacle", StringComparison.Ordinal))
                            UnityEngine.Object.DestroyImmediate(component);
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, NpcPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void RemovePhoneEndControls()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PhonePrefabPath);
            try
            {
                foreach (string objectName in new[]
                         { "Reject Button", "Reject Label", "Hang Up Button", "Hang Up Label" })
                {
                    Transform child = FindChildRecursive(root.transform, objectName);
                    if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
                RectTransform accept = FindChildRecursive(root.transform, "Accept Button") as RectTransform;
                RectTransform acceptLabel = FindChildRecursive(root.transform, "Accept Label") as RectTransform;
                if (accept != null) accept.anchoredPosition = new Vector2(0f, -93f);
                if (acceptLabel != null) acceptLabel.anchoredPosition = new Vector2(0f, -137f);
                PrefabUtility.SaveAsPrefabAsset(root, PhonePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static T FindFirst<T>() where T : UnityEngine.Object =>
            UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);

        private static T[] FindObjects<T>() where T : UnityEngine.Object =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        private static Transform CreatePoint(string name, Transform parent, Vector3 position)
        {
            GameObject point = new(name);
            point.transform.SetParent(parent, false);
            point.transform.position = position;
            return point.transform;
        }

        private static Transform RequireTemplatePoint(Transform template, string pointName)
        {
            Transform point = template.Find(pointName);
            if (point == null)
                throw new InvalidOperationException(
                    $"Template is missing the required route marker '{pointName}'.");
            return point;
        }

        private static Vector3 Grounded(Vector3 position) =>
            new(position.x, 0f, position.z);

        private static Vector3 AtTriggerHeight(Vector3 position) =>
            new(position.x, 1f, position.z);

        private static Vector3 Offset(Vector3 origin, float x, float z) =>
            new(origin.x + x, 0f, origin.z + z);

        private static GameObject CreateGroup(string name, Transform parent)
        {
            GameObject group = new(name);
            group.transform.SetParent(parent, false);
            return group;
        }

        private static GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material,
            bool keepCollider)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static GameObject CreateLocalCube(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject cube = CreateCube(name, parent, Vector3.zero, localScale, material, false);
            cube.transform.localPosition = localPosition;
            return cube;
        }

        private static Material GetOrCreateMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void RemoveObsoleteRouteMaterials()
        {
            AssetDatabase.DeleteAsset($"{MaterialFolder}/Training Lane.mat");
            AssetDatabase.DeleteAsset($"{MaterialFolder}/Impact Barrier.mat");
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static void SetObjectArray<T>(SerializedProperty property, T[] values)
            where T : UnityEngine.Object
        {
            property.arraySize = values?.Length ?? 0;
            for (int index = 0; index < property.arraySize; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }

        private static List<T> ReadObjectArray<T>(SerializedProperty property)
            where T : UnityEngine.Object
        {
            List<T> values = new();
            for (int index = 0; index < property.arraySize; index++)
            {
                T value = property.GetArrayElementAtIndex(index).objectReferenceValue as T;
                if (value != null && !values.Contains(value)) values.Add(value);
            }
            return values;
        }

        private static Transform FindChildRecursive(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        private static T FindNamedComponent<T>(Transform root, string name) where T : Component
        {
            Transform child = FindChildRecursive(root, name);
            return child != null ? child.GetComponent<T>() : null;
        }

        private static void DisableNamedChild(Transform root, string name)
        {
            Transform child = FindChildRecursive(root, name);
            if (child != null) child.gameObject.SetActive(false);
        }

        private static void RemoveLocalization(GameObject gameObject)
        {
            Component localization = gameObject.GetComponents<Component>()
                .FirstOrDefault(component => component != null &&
                    component.GetType().FullName == "ElectricPalletStackers.Localization.LocalizedText");
            if (localization != null) UnityEngine.Object.DestroyImmediate(localization);
        }
    }
}
