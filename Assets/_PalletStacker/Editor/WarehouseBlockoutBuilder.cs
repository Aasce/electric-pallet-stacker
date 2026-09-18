using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElectricPalletStackers.EditorTools
{
    public static class WarehouseBlockoutBuilder
    {
        private const string ScenePath = "Assets/Scenes/MainScene.unity";
        private const string EnvironmentRootName = "Enviroments";
        private const string BlockoutRootName = "Warehouse Blockout";
        private const string MaterialFolder = "Assets/_PalletStacker/Environment/Materials";

        [MenuItem("Tools/Electric Pallet Stacker/Rebuild Warehouse Blockout")]
        public static void BuildMainScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var environmentRoot = GameObject.Find(EnvironmentRootName);

            if (environmentRoot == null)
            {
                environmentRoot = new GameObject(EnvironmentRootName);
            }

            var existing = environmentRoot.transform.Find(BlockoutRootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            EnsureFolder(MaterialFolder);
            var wallMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Warehouse Wall.mat",
                new Color(0.34f, 0.38f, 0.42f));
            var obstacleMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Shelf Obstacle.mat",
                new Color(0.16f, 0.27f, 0.34f));
            var platformMaterial = GetOrCreateMaterial(
                $"{MaterialFolder}/Pallet Platform.mat",
                new Color(0.88f, 0.55f, 0.10f));

            var blockoutRoot = CreateGroup(BlockoutRootName, environmentRoot.transform);
            var walls = CreateGroup("Walls (4 Cubes)", blockoutRoot.transform);
            var obstacles = CreateGroup("Shelf Obstacles (4 Cubes)", blockoutRoot.transform);
            var platforms = CreateGroup("Raised Pallet Platforms", blockoutRoot.transform);

            // Four perimeter walls. The scene intentionally has no ceiling.
            CreateCube("Wall North", walls.transform, new Vector3(0f, 2f, 11f), new Vector3(22.5f, 4f, 0.35f), wallMaterial);
            CreateCube("Wall South", walls.transform, new Vector3(0f, 2f, -11f), new Vector3(22.5f, 4f, 0.35f), wallMaterial);
            CreateCube("Wall East", walls.transform, new Vector3(11f, 2f, 0f), new Vector3(0.35f, 4f, 22.5f), wallMaterial);
            CreateCube("Wall West", walls.transform, new Vector3(-11f, 2f, 0f), new Vector3(0.35f, 4f, 22.5f), wallMaterial);

            // Four large shelf-like obstacles, kept near the sides so the central driving lane stays clear.
            CreateCube("Shelf Obstacle 01", obstacles.transform, new Vector3(-7f, 1.5f, 5.5f), new Vector3(3.2f, 3f, 1.8f), obstacleMaterial);
            CreateCube("Shelf Obstacle 02", obstacles.transform, new Vector3(7f, 1.5f, 5.5f), new Vector3(3.2f, 3f, 1.8f), obstacleMaterial);
            CreateCube("Shelf Obstacle 03", obstacles.transform, new Vector3(-7f, 1.5f, -4.5f), new Vector3(3.2f, 3f, 1.8f), obstacleMaterial);
            CreateCube("Shelf Obstacle 04", obstacles.transform, new Vector3(7f, 1.5f, -4.5f), new Vector3(3.2f, 3f, 1.8f), obstacleMaterial);

            // Three waist-height pallet bays along the inside of the south wall.
            CreateCube("Pallet Platform 01", platforms.transform, new Vector3(-6f, 0.5f, -9.5f), new Vector3(2.6f, 1f, 2.4f), platformMaterial);
            CreateCube("Pallet Platform 02", platforms.transform, new Vector3(0f, 0.5f, -9.5f), new Vector3(2.6f, 1f, 2.4f), platformMaterial);
            CreateCube("Pallet Platform 03", platforms.transform, new Vector3(6f, 0.5f, -9.5f), new Vector3(2.6f, 1f, 2.4f), platformMaterial);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Warehouse blockout built: 4 walls, 4 shelf obstacles, 3 raised pallet platforms, no ceiling.");
        }

        private static GameObject CreateGroup(string name, Transform parent)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group;
        }

        private static GameObject CreateCube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = scale;

            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            GameObjectUtility.SetStaticEditorFlags(
                cube,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic);

            return cube;
        }

        private static Material GetOrCreateMaterial(string path, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.22f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string folderPath)
        {
            var parts = folderPath.Split('/');
            var current = parts[0];

            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
