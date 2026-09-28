using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElectricPalletStackers.Localization
{
    [DisallowMultipleComponent]
    public sealed class LocalizationManager : MonoBehaviour, ILocalizedViewRegistry
    {
        private const string DefaultDatabaseResourcePath =
            "Localization/DefaultLocalizationDatabase";

        [SerializeField] private LocalizationDatabase _database;
        [SerializeField] private LanguageCode _defaultLanguage = LanguageCode.En;

        private readonly List<ILocalizedView> _views = new();
        private LocalizationService _service;
        private bool _initialized;

        public ILocalizationService Service => _service;
        public LanguageCode CurrentLanguage =>
            _service != null ? _service.CurrentLanguage : _defaultLanguage;

        public event Action<LanguageCode> LanguageChanged;

        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            if (_service != null) _service.LanguageChanged -= HandleLanguageChanged;
        }

        public void Initialize()
        {
            if (_initialized) return;

            if (_database == null)
                _database = Resources.Load<LocalizationDatabase>(DefaultDatabaseResourcePath);

            if (_database == null)
            {
                Debug.LogError(
                    $"Localization database was not found at Resources/{DefaultDatabaseResourcePath}.",
                    this);
                return;
            }

            _service = new LocalizationService(_database, _defaultLanguage);
            _service.LanguageChanged += HandleLanguageChanged;
            _initialized = true;
            RefreshAll();
        }

        public void SetLanguage(LanguageCode language)
        {
            Initialize();
            if (_service == null) return;

            if (_service.CurrentLanguage == language)
            {
                RefreshAll();
                return;
            }

            _service.SetLanguage(language);
        }

        public string GetText(string key)
        {
            Initialize();
            return _service != null ? _service.GetText(key) : $"[{key}]";
        }

        public void Register(ILocalizedView view)
        {
            if (view == null || _views.Contains(view)) return;
            _views.Add(view);
            if (_service != null) view.ApplyLocalization(_service);
        }

        public void Unregister(ILocalizedView view)
        {
            if (view != null) _views.Remove(view);
        }

        public void RefreshAll()
        {
            if (_service == null) return;
            DiscoverSceneViews();

            for (int index = _views.Count - 1; index >= 0; index--)
            {
                ILocalizedView view = _views[index];
                if (view is UnityEngine.Object unityObject && unityObject == null)
                {
                    _views.RemoveAt(index);
                    continue;
                }

                view.ApplyLocalization(_service);
            }
        }

        private void HandleLanguageChanged(LanguageCode language)
        {
            RefreshAll();
            LanguageChanged?.Invoke(language);
        }

        private void DiscoverSceneViews()
        {
            Scene owningScene = gameObject.scene;
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int index = 0; index < behaviours.Length; index++)
            {
                MonoBehaviour behaviour = behaviours[index];
                if (behaviour == null || behaviour.gameObject.scene != owningScene) continue;
                if (behaviour is ILocalizedView view && !_views.Contains(view)) _views.Add(view);
            }
        }
    }
}
