# Localization

The UI localization flow is split into four responsibilities:

1. `LocalizationDatabase` stores localized strings and per-language font overrides.
2. `LocalizationService` resolves keys, applies the English fallback, and owns the selected language.
3. `LocalizationManager` is the Unity composition root. It creates the service, discovers/registers
   `ILocalizedView` implementations in its scene, and refreshes them when the language changes.
4. `LocalizedText` is a small TextMesh Pro adapter. Dynamic views such as
   `PhoneCallPanel` implement `ILocalizedView` directly.

The three language-choice button labels are intentionally not localized. They always remain
`English`, `Tiếng Việt`, and `日本語`, while the surrounding title and instructions follow the
currently focused language.

`UIController` only coordinates the UI flow. When the focused language changes on the language
selection panel, it delegates the change to `LocalizationManager`; it does not read translation data.

## Add or edit a string

1. Add a constant to `LocalizationKeys`.
2. Add the translations to
   `Resources/Localization/DefaultLocalizationDatabase.asset`.
3. Add `LocalizedText` to a static TMP label and assign the key. For text whose value also changes at
   runtime, implement `ILocalizedView` on the owning view instead.

Missing text falls back to English. If the key is absent from every language, the service renders
`[key]` so configuration errors remain visible during development.

## Add a language

1. Add the language to `LanguageCode`.
2. Add its serialized value to `LocalizedStringEntry` and return it from `GetText`.
3. Add a language profile to the database when that writing system needs a TMP font override.
4. Add the corresponding choice to `UISelectLanguagePanel`.

The editor command **Tools > Electric Pallet Stacker > Localization > Setup or Repair** can recreate
the default database and required prefab bindings if they are missing. It never overwrites an existing
database, so translation edits remain safe.
