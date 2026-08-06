using System;
using MelonLoader;
using UnityEngine;

namespace SMBZG.Accessibility
{
    /// <summary>MelonPreferences-backed settings for the accessibility mod.</summary>
    internal static class Settings
    {
        private static MelonPreferences_Category _category;

        private static MelonPreferences_Entry<bool> _speechEnabled;
        private static MelonPreferences_Entry<bool> _announceFocus;
        private static MelonPreferences_Entry<bool> _announceBattleEvents;
        private static MelonPreferences_Entry<bool> _periodicStatus;
        private static MelonPreferences_Entry<float> _periodicStatusInterval;
        private static MelonPreferences_Entry<bool> _periodicSpatial;
        private static MelonPreferences_Entry<float> _periodicSpatialInterval;

        private static MelonPreferences_Entry<string> _hotkeyStatus;
        private static MelonPreferences_Entry<string> _hotkeySpatial;
        private static MelonPreferences_Entry<string> _hotkeyTimer;
        private static MelonPreferences_Entry<string> _hotkeyHelp;
        private static MelonPreferences_Entry<string> _hotkeyRemapP1;
        private static MelonPreferences_Entry<string> _hotkeyRemapP2;

        public static void Init()
        {
            if (_category != null)
            {
                return;
            }
            _category = MelonPreferences.CreateCategory("SMBZG_Accessibility", "SMBZG Accessibility");

            _speechEnabled = _category.CreateEntry<bool>("SpeechEnabled", true, "Speech enabled",
                "Master toggle for all NVDA narration.");
            _announceFocus = _category.CreateEntry<bool>("AnnounceFocus", true, "Announce menu focus",
                "Narrate menu items as keyboard focus moves through menus.");
            _announceBattleEvents = _category.CreateEntry<bool>("AnnounceBattleEvents", true, "Announce battle events",
                "Narrate fight start, pause/resume, hits, combos, rushes, guards and KO's.");
            _periodicStatus = _category.CreateEntry<bool>("PeriodicStatus", true, "Periodic status readout",
                "Continuously report both fighters' health, energy and stun during battle.");
            _periodicStatusInterval = _category.CreateEntry<float>("PeriodicStatusInterval", 12f, "Status readout interval (seconds)",
                "How often the full status readout is announced during battle.");
            _periodicSpatial = _category.CreateEntry<bool>("PeriodicSpatial", false, "Periodic spatial readout",
                "Continuously report fighter positions and distance.");
            _periodicSpatialInterval = _category.CreateEntry<float>("PeriodicSpatialInterval", 20f, "Spatial readout interval (seconds)",
                "How often the spatial readout is announced during battle.");

            _hotkeyStatus = _category.CreateEntry<string>("HotkeyStatus", "F5", "Hotkey: status",
                "Announce full battle status on demand.");
            _hotkeySpatial = _category.CreateEntry<string>("HotkeySpatial", "F6", "Hotkey: spatial",
                "Announce fighter positions and distance on demand.");
            _hotkeyTimer = _category.CreateEntry<string>("HotkeyTimer", "F7", "Hotkey: round timer",
                "Announce the current round time on demand.");
            _hotkeyHelp = _category.CreateEntry<string>("HotkeyHelp", "F8", "Hotkey: controls help",
                "Announce the active input bindings for both players.");
            _hotkeyRemapP1 = _category.CreateEntry<string>("HotkeyRemapP1", "F9", "Hotkey: remap player 1 (hold Ctrl)",
                "Hold Control and press this key to open the keyboard remapping flow for player 1.");
            _hotkeyRemapP2 = _category.CreateEntry<string>("HotkeyRemapP2", "F10", "Hotkey: remap player 2 (hold Ctrl)",
                "Hold Control and press this key to open the keyboard remapping flow for player 2.");
        }

        public static bool SpeechEnabled { get { return _speechEnabled != null && _speechEnabled.Value; } }
        public static bool AnnounceFocus { get { return _announceFocus == null || _announceFocus.Value; } }
        public static bool AnnounceBattleEvents { get { return _announceBattleEvents == null || _announceBattleEvents.Value; } }
        public static bool PeriodicStatus { get { return _periodicStatus == null || _periodicStatus.Value; } }
        public static float PeriodicStatusInterval { get { return _periodicStatusInterval == null ? 12f : Mathf.Max(2f, _periodicStatusInterval.Value); } }
        public static bool PeriodicSpatial { get { return _periodicSpatial != null && _periodicSpatial.Value; } }
        public static float PeriodicSpatialInterval { get { return _periodicSpatialInterval == null ? 20f : Mathf.Max(5f, _periodicSpatialInterval.Value); } }

        public static KeyCode HotkeyStatus { get { return ParseKey(_hotkeyStatus, KeyCode.F5); } }
        public static KeyCode HotkeySpatial { get { return ParseKey(_hotkeySpatial, KeyCode.F6); } }
        public static KeyCode HotkeyTimer { get { return ParseKey(_hotkeyTimer, KeyCode.F7); } }
        public static KeyCode HotkeyHelp { get { return ParseKey(_hotkeyHelp, KeyCode.F8); } }
        public static KeyCode HotkeyRemapP1 { get { return ParseKey(_hotkeyRemapP1, KeyCode.F9); } }
        public static KeyCode HotkeyRemapP2 { get { return ParseKey(_hotkeyRemapP2, KeyCode.F10); } }

        private static KeyCode ParseKey(MelonPreferences_Entry<string> entry, KeyCode fallback)
        {
            if (entry == null)
            {
                return fallback;
            }
            string name = entry.Value;
            if (string.IsNullOrEmpty(name))
            {
                return fallback;
            }
            try
            {
                object parsed = Enum.Parse(typeof(KeyCode), name, true);
                return (KeyCode)parsed;
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
