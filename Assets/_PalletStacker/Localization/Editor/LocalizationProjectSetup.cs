#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using ElectricPalletStackers.Localization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace ElectricPalletStackers.Editor
{
    public static class LocalizationProjectSetup
    {
        private const string DatabaseFolder =
            "Assets/_PalletStacker/Resources/Localization";
        private const string DatabasePath =
            DatabaseFolder + "/DefaultLocalizationDatabase.asset";
        private const string SourceFontPath =
            "Assets/_PalletStacker/Localization/Fonts/NotoSansJP-Variable.ttf";
        private const string JapaneseFontAssetPath =
            "Assets/_PalletStacker/Localization/Fonts/NotoSansJP Dynamic SDF.asset";

        private static readonly TranslationDefinition[] DefaultTranslations =
        {
            new(LocalizationKeys.Common.Confirm,
                "Press E-Stop to Confirm",
                "Nhấn dừng khẩn cấp để xác nhận",
                "非常停止ボタンで決定"),
            new(LocalizationKeys.Common.Continue,
                "Press E-Stop to Continue",
                "Nhấn dừng khẩn cấp để tiếp tục",
                "非常停止ボタンで続行"),
            new(LocalizationKeys.Language.English,
                "English", "Tiếng Anh", "英語"),
            new(LocalizationKeys.Language.Vietnamese,
                "Vietnamese", "Tiếng Việt", "ベトナム語"),
            new(LocalizationKeys.Language.Japanese,
                "Japanese", "Tiếng Nhật", "日本語"),
            new(LocalizationKeys.SelectLanguage.Title,
                "Select Language", "Chọn ngôn ngữ", "言語を選択"),
            new(LocalizationKeys.SelectLanguage.NavigationHint,
                "Use Travel to select language",
                "Dùng cần di chuyển để chọn ngôn ngữ",
                "走行レバーで言語を選択"),
            new(LocalizationKeys.Welcome.Title,
                "Welcome", "Chào mừng", "ようこそ"),
            new(LocalizationKeys.Welcome.Description,
                "This training simulation recreates accident scenarios involving a walkie pallet stacker.",
                "Mô phỏng huấn luyện này tái hiện các tình huống tai nạn liên quan đến xe nâng tay cao chạy điện.",
                "この訓練シミュレーションでは、歩行操作式パレットスタッカーに関する事故状況を再現します。"),
            new(LocalizationKeys.Guide.Title,
                "Guide", "Hướng dẫn", "操作ガイド"),
            new(LocalizationKeys.Guide.ScrollHint,
                "Use Travel to scroll",
                "Dùng cần di chuyển để cuộn",
                "走行レバーでスクロール"),
            new(LocalizationKeys.Completed.Title,
                "Completed", "Hoàn thành", "完了"),
            new(LocalizationKeys.Completed.Description,
                "You have successfully completed the training simulation.",
                "Bạn đã hoàn thành mô phỏng huấn luyện.",
                "訓練シミュレーションを完了しました。"),
            new(LocalizationKeys.Failed.Title,
                "Failed", "Không đạt", "失敗"),
            new(LocalizationKeys.Failed.Description,
                "A collision was detected. The training simulation has failed.",
                "Đã phát hiện va chạm. Mô phỏng huấn luyện không đạt.",
                "衝突を検知しました。訓練シミュレーションは失敗です。"),
            new(LocalizationKeys.Phone.IncomingCall,
                "Incoming call", "Cuộc gọi đến", "着信"),
            new(LocalizationKeys.Phone.InCall,
                "In call", "Đang gọi", "通話中"),
            new(LocalizationKeys.Phone.TapToAnswer,
                "Tap to answer", "Chạm để trả lời", "タップして応答"),
            new(LocalizationKeys.Phone.Decline,
                "Decline", "Từ chối", "拒否"),
            new(LocalizationKeys.Phone.Accept,
                "Accept", "Chấp nhận", "応答"),
            new(LocalizationKeys.Phone.End,
                "End", "Kết thúc", "終了")
        };

        private static readonly PrefabDefinition[] Prefabs =
        {
            new("Assets/_PalletStacker/UI/Prefabs/UI Select Language Panel.prefab",
                new TextBinding("Confirm Hint", LocalizationKeys.Common.Confirm),
                new TextBinding("Previous Hint", LocalizationKeys.SelectLanguage.NavigationHint),
                new TextBinding("Title", LocalizationKeys.SelectLanguage.Title)),
            new("Assets/_PalletStacker/UI/Prefabs/UI Welcome Panel.prefab",
                new TextBinding("Title", LocalizationKeys.Welcome.Title),
                new TextBinding("Description", LocalizationKeys.Welcome.Description),
                new TextBinding("Confirm Hint", LocalizationKeys.Common.Continue)),
            new("Assets/_PalletStacker/UI/Prefabs/UI Guide Panel.prefab",
                new TextBinding("Title", LocalizationKeys.Guide.Title),
                new TextBinding("Confirm Hint", LocalizationKeys.Common.Continue),
                new TextBinding("Scroll Hint", LocalizationKeys.Guide.ScrollHint)),
            new("Assets/_PalletStacker/UI/Prefabs/UI Completed Panel.prefab",
                new TextBinding("Title", LocalizationKeys.Completed.Title),
                new TextBinding("Description", LocalizationKeys.Completed.Description),
                new TextBinding("Confirm Hint", LocalizationKeys.Common.Continue)),
            new("Assets/_PalletStacker/UI/Prefabs/UI Failed Panel.prefab",
                new TextBinding("Title", LocalizationKeys.Failed.Title),
                new TextBinding("Description", LocalizationKeys.Failed.Description),
                new TextBinding("Confirm Hint", LocalizationKeys.Common.Continue)),
            new("Assets/_PalletStacker/UI/Prefabs/Phone Call Panel.prefab",
                new TextBinding("Reject Label", LocalizationKeys.Phone.Decline),
                new TextBinding("Accept Label", LocalizationKeys.Phone.Accept),
                new TextBinding("Hang Up Label", LocalizationKeys.Phone.End))
        };

        [InitializeOnLoadMethod]
        private static void ScheduleAutomaticSetup()
        {
            EditorApplication.delayCall += EnsureConfigured;
        }

        [MenuItem("Tools/Electric Pallet Stacker/Localization/Setup or Repair")]
        public static void SetupOrRepair()
        {
            EnsureConfigured();
            Debug.Log("Localization setup is complete.");
        }

        private static void EnsureConfigured()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureConfigured;
                return;
            }

            TMP_FontAsset japaneseFont = CreateOrLoadJapaneseFont();
            CreateDatabaseIfMissing(japaneseFont);

            for (int index = 0; index < Prefabs.Length; index++)
                ConfigurePrefab(Prefabs[index]);

            AssetDatabase.SaveAssets();
        }

        private static TMP_FontAsset CreateOrLoadJapaneseFont()
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                JapaneseFontAssetPath);
            if (existing != null) return existing;

            Font source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (source == null)
            {
                Debug.LogError($"Japanese source font is missing: {SourceFontPath}");
                return null;
            }

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                source,
                90,
                9,
                GlyphRenderMode.SDFAA,
                2048,
                2048,
                AtlasPopulationMode.Dynamic,
                true);
            fontAsset.name = "NotoSansJP Dynamic SDF";
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.isMultiAtlasTexturesEnabled = true;

            AssetDatabase.CreateAsset(fontAsset, JapaneseFontAssetPath);
            if (fontAsset.atlasTextures != null)
            {
                for (int index = 0; index < fontAsset.atlasTextures.Length; index++)
                {
                    Texture2D texture = fontAsset.atlasTextures[index];
                    if (texture != null && !AssetDatabase.Contains(texture))
                        AssetDatabase.AddObjectToAsset(texture, fontAsset);
                }
            }
            if (fontAsset.material != null && !AssetDatabase.Contains(fontAsset.material))
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.ImportAsset(JapaneseFontAssetPath);
            return fontAsset;
        }

        private static void CreateDatabaseIfMissing(TMP_FontAsset japaneseFont)
        {
            if (AssetDatabase.LoadAssetAtPath<LocalizationDatabase>(DatabasePath) != null)
                return;

            EnsureFolder("Assets/_PalletStacker/Resources");
            EnsureFolder(DatabaseFolder);

            LocalizationDatabase database = ScriptableObject.CreateInstance<LocalizationDatabase>();
            AssetDatabase.CreateAsset(database, DatabasePath);

            SerializedObject serializedDatabase = new(database);
            SerializedProperty entries = serializedDatabase.FindProperty("_entries");
            entries.arraySize = DefaultTranslations.Length;

            for (int index = 0; index < DefaultTranslations.Length; index++)
            {
                TranslationDefinition definition = DefaultTranslations[index];
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("_key").stringValue = definition.Key;
                entry.FindPropertyRelative("_english").stringValue = definition.English;
                entry.FindPropertyRelative("_vietnamese").stringValue = definition.Vietnamese;
                entry.FindPropertyRelative("_japanese").stringValue = definition.Japanese;
            }

            SerializedProperty profiles = serializedDatabase.FindProperty("_languageProfiles");
            profiles.arraySize = japaneseFont != null ? 1 : 0;
            if (japaneseFont != null)
            {
                SerializedProperty japaneseProfile = profiles.GetArrayElementAtIndex(0);
                japaneseProfile.FindPropertyRelative("_language").enumValueIndex =
                    (int)LanguageCode.Ja;
                japaneseProfile.FindPropertyRelative("_fontOverride").objectReferenceValue =
                    japaneseFont;
            }

            serializedDatabase.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        private static void ConfigurePrefab(PrefabDefinition definition)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(definition.Path);
            try
            {
                bool changed = false;
                TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
                Dictionary<string, TMP_Text> textByName = new(StringComparer.Ordinal);
                for (int index = 0; index < texts.Length; index++)
                {
                    TMP_Text text = texts[index];
                    if (!textByName.ContainsKey(text.name)) textByName.Add(text.name, text);
                }

                for (int index = 0; index < definition.Bindings.Length; index++)
                {
                    TextBinding binding = definition.Bindings[index];
                    if (!textByName.TryGetValue(binding.ObjectName, out TMP_Text target))
                    {
                        Debug.LogError(
                            $"Localization target '{binding.ObjectName}' was not found in {definition.Path}.");
                        continue;
                    }

                    LocalizedText localizedText = target.GetComponent<LocalizedText>();
                    if (localizedText == null)
                    {
                        localizedText = target.gameObject.AddComponent<LocalizedText>();
                        changed = true;
                    }

                    SerializedObject serializedText = new(localizedText);
                    SerializedProperty targetProperty = serializedText.FindProperty("_target");
                    SerializedProperty keyProperty = serializedText.FindProperty("_key");
                    if (targetProperty.objectReferenceValue != target)
                    {
                        targetProperty.objectReferenceValue = target;
                        changed = true;
                    }

                    if (!string.Equals(keyProperty.stringValue, binding.Key, StringComparison.Ordinal))
                    {
                        keyProperty.stringValue = binding.Key;
                        changed = true;
                    }

                    serializedText.ApplyModifiedPropertiesWithoutUndo();
                }

                if (changed) PrefabUtility.SaveAsPrefabAsset(root, definition.Path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            string folderName = path.Substring(separator + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private readonly struct TranslationDefinition
        {
            public TranslationDefinition(
                string key,
                string english,
                string vietnamese,
                string japanese)
            {
                Key = key;
                English = english;
                Vietnamese = vietnamese;
                Japanese = japanese;
            }

            public string Key { get; }
            public string English { get; }
            public string Vietnamese { get; }
            public string Japanese { get; }
        }

        private readonly struct TextBinding
        {
            public TextBinding(string objectName, string key)
            {
                ObjectName = objectName;
                Key = key;
            }

            public string ObjectName { get; }
            public string Key { get; }
        }

        private readonly struct PrefabDefinition
        {
            public PrefabDefinition(string path, params TextBinding[] bindings)
            {
                Path = path;
                Bindings = bindings;
            }

            public string Path { get; }
            public TextBinding[] Bindings { get; }
        }
    }
}
#endif
