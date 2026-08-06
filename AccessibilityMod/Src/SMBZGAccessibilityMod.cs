using System;
using MelonLoader;
using TeamUtility.IO;
using UnityEngine;

[assembly: MelonInfo(typeof(SMBZG.Accessibility.SMBZGAccessibilityMod), "SMBZG Accessibility", "1.0.0", "opencode")]
[assembly: MelonPriority(1000)]

namespace SMBZG.Accessibility
{
    /// <summary>
    /// Accessibility mod for SMBZ-G that narrates menus and fights through the NVDA
    /// screen reader and provides a keyboard-only input remapping flow.
    /// </summary>
    public class SMBZGAccessibilityMod : MelonMod
    {
        private bool _pendingInitAnnounce = true;
        private float _nextNvdaCheck;
        private bool _lastNvdaAvailable;

        public override void OnInitializeMelon()
        {
            Settings.Init();
            Speaker.Configure(() => Settings.SpeechEnabled, text => LoggerInstance.Msg("[Speak] " + text));
            NvdaController.Init();
            BattleAnnouncer.Initialize();
            _lastNvdaAvailable = NvdaController.IsNvdaRunning();
            LoggerInstance.Msg("SMBZG Accessibility mod initialized.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            try
            {
                UiNarrator.OnSceneChanged(sceneName);
                BattleAnnouncer.OnSceneChanged(sceneName);
                MainMenuWatcher.OnSceneChanged(sceneName);
                Remapper.OnSceneChanged();
                EditControls.OnSceneChanged();
                AnnounceScene(sceneName);
            }
            catch (Exception ex)
            {
                LoggerInstance.Error("OnSceneWasLoaded failed: " + ex);
            }
        }

        public override void OnUpdate()
        {
            try
            {
                if (_pendingInitAnnounce)
                {
                    _pendingInitAnnounce = false;
                    if (_lastNvdaAvailable)
                    {
                        Speaker.Speak("Accessibility mod ready.", "status", SpeechPriority.Status, 0f);
                    }
                    else
                    {
                        LoggerInstance.Warning("NVDA is not currently running; speech will be unavailable until NVDA starts.");
                    }
                }

                float now = Time.realtimeSinceStartup;
                if (now >= _nextNvdaCheck)
                {
                    _nextNvdaCheck = now + 4f;
                    bool available = NvdaController.IsNvdaRunning();
                    if (available && !_lastNvdaAvailable)
                    {
                        Speaker.InterruptNow("NVDA connected.", "status");
                    }
                    _lastNvdaAvailable = available;
                }

                HandleHotkeys();
                Remapper.Update();
                if (!Remapper.IsActive)
                {
                    UiNarrator.Update();
                    BattleAnnouncer.Update();
                }
                MainMenuWatcher.Update();
                Speaker.Process();
            }
            catch (Exception ex)
            {
                LoggerInstance.Error("OnUpdate failed: " + ex);
            }
        }

        private void HandleHotkeys()
        {
            if (Remapper.IsActive)
            {
                return;
            }
            bool ctrl = Remapper.ModifierHeld;
            if (ctrl && Input.GetKeyDown(Settings.HotkeyRemapP1))
            {
                Remapper.Toggle(PlayerID.One);
                return;
            }
            if (ctrl && Input.GetKeyDown(Settings.HotkeyRemapP2))
            {
                Remapper.Toggle(PlayerID.Two);
                return;
            }
            if (Input.GetKeyDown(Settings.HotkeyStatus))
            {
                BattleAnnouncer.AnnounceStatus();
                return;
            }
            if (Input.GetKeyDown(Settings.HotkeySpatial))
            {
                BattleAnnouncer.AnnounceSpatial();
                return;
            }
            if (Input.GetKeyDown(Settings.HotkeyTimer))
            {
                BattleAnnouncer.AnnounceTimer();
                return;
            }
            if (Input.GetKeyDown(Settings.HotkeyHelp))
            {
                Speaker.Speak("F5 status. F6 spatial. F7 timer. F8 this help. Hold Control and press F9 or F10 to remap keyboard controls for player 1 or 2.", "help", SpeechPriority.Status, 0f);
                Remapper.AnnounceBindings(PlayerID.One);
                Remapper.AnnounceBindings(PlayerID.Two);
            }
        }

        private static void AnnounceScene(string sceneName)
        {
            MelonLogger.Msg("[AnnounceScene] scene=\"" + sceneName + "\"");
            string message;
            switch (sceneName)
            {
                case SceneConstants.MainMenu:
                    message = "Main menu.";
                    break;
                case SceneConstants.CharacterSelect:
                case SceneConstants.CharacterSelect_Arcade:
                    message = "Character select. Use arrow keys to pick a character, Enter to confirm.";
                    break;
                case SceneConstants.Battle:
                    message = "Battle.";
                    break;
                case SceneConstants.BattleResults:
                case SceneConstants.BattleResults_ArcadeReview:
                    message = "Battle results.";
                    break;
                default:
                    message = "Screen changed.";
                    break;
            }
            Speaker.InterruptNow(message, "scene");
        }
    }
}
