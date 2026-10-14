using System;
using System.Collections.Generic;
using ElectricPalletStackers.Gameplay;
using ElectricPalletStackers.Localization;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [CreateAssetMenu(
        fileName = "Game Failure Message Catalog",
        menuName = "Electric Pallet Stacker/Game Failure Message Catalog")]
    public sealed class GameFailureMessageCatalog : ScriptableObject
    {
        [SerializeField] private List<GameFailureMessageEntry> _entries = new();

        public bool TryGet(
            GameFailureReason reason,
            LanguageCode language,
            out GameFailureMessage message)
        {
            for (int index = 0; index < _entries.Count; index++)
            {
                GameFailureMessageEntry entry = _entries[index];
                if (entry == null || entry.Reason != reason) continue;

                message = new GameFailureMessage(
                    entry.Cause.GetText(language),
                    entry.Guidance.GetText(language));
                return true;
            }

            message = default;
            return false;
        }
    }

    public readonly struct GameFailureMessage
    {
        public GameFailureMessage(string cause, string guidance)
        {
            Cause = cause;
            Guidance = guidance;
        }

        public string Cause { get; }
        public string Guidance { get; }
    }

    [Serializable]
    public sealed class GameFailureMessageEntry
    {
        [SerializeField] private GameFailureReason _reason;
        [SerializeField] private LocalizedFailureText _cause = new();
        [SerializeField] private LocalizedFailureText _guidance = new();

        public GameFailureReason Reason => _reason;
        public LocalizedFailureText Cause => _cause;
        public LocalizedFailureText Guidance => _guidance;
    }

    [Serializable]
    public sealed class LocalizedFailureText
    {
        [SerializeField, TextArea(2, 5)] private string _english;
        [SerializeField, TextArea(2, 5)] private string _vietnamese;
        [SerializeField, TextArea(2, 5)] private string _japanese;

        public string GetText(LanguageCode language)
        {
            return language switch
            {
                LanguageCode.Vi => _vietnamese,
                LanguageCode.Ja => _japanese,
                _ => _english
            };
        }
    }
}
