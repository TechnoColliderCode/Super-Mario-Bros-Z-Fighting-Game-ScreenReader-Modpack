using System;
using System.Collections;
using MelonLoader;
using TeamUtility.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SMBZG.Accessibility
{
    /// <summary>
    /// Prevents the game's "Press any key" screen from getting stuck forever.
    /// The game's player-one input scan only lasts 10 seconds; if no key is
    /// pressed in that window the scan silently expires and no key will ever
    /// advance the game. This watches for that state and restarts the scan
    /// using the game's own routine so a keyboard-only user always has an
    /// active scan to press into. It also handles the fresh-install language
    /// selection page so keyboard navigation works there too.
    /// </summary>
    internal static class MainMenuWatcher
    {
        private static string _lastScene = string.Empty;
        private static float _nextCheck;
        private static float _nextRestart;
        private static bool _scanWasActive;
        private static bool _wasPressAnyKeyVisible;
        private static bool _wasLanguageVisible;
        private static bool _pendingStart;
        private static KeyCode[] _keyCodes;
        private static Button _languageEnglish;
        private static Button _languageSpanish;

        public static void OnSceneChanged(string sceneName)
        {
            _lastScene = sceneName ?? string.Empty;
            _nextCheck = 0f;
            _nextRestart = 0f;
            _scanWasActive = false;
            _wasPressAnyKeyVisible = false;
            _wasLanguageVisible = false;
            _pendingStart = false;
        }

        public static void Update()
        {
            if (_lastScene != SceneConstants.MainMenu)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextCheck)
            {
                return;
            }
            _nextCheck = now + 0.5f;

            MainMenuScript menu = UnityEngine.Object.FindObjectOfType<MainMenuScript>();
            if (menu == null)
            {
                return;
            }

            bool languageVisible = menu.LanguageSelectionPage != null && menu.LanguageSelectionPage.activeInHierarchy;
            bool pressAnyKeyVisible = menu.PressAnyKeyText != null && menu.PressAnyKeyText.gameObject.activeInHierarchy;

            if (pressAnyKeyVisible != _wasPressAnyKeyVisible || languageVisible != _wasLanguageVisible)
            {
                MelonLogger.Msg("[MainMenuWatcher] pressAnyKey=" + pressAnyKeyVisible +
                    " language=" + languageVisible +
                    " scanning=" + InputManager.IsScanning +
                    " device0=\"" + (GC.ins == null ? "null" : GC.ins.PlayerInputDevices[0] ?? "empty") + "\"");
            }

            HandleLanguagePage(menu, languageVisible);
            HandlePressAnyKey(menu, pressAnyKeyVisible, now);
        }

        private static void HandleLanguagePage(MainMenuScript menu, bool visible)
        {
            if (!visible)
            {
                _wasLanguageVisible = false;
                _languageEnglish = null;
                _languageSpanish = null;
                return;
            }

            if (!_wasLanguageVisible)
            {
                _wasLanguageVisible = true;
                LocateLanguageButtons(menu);
                SelectLanguage(_languageEnglish != null ? _languageEnglish : _languageSpanish);
                Speaker.Speak("Language selection. Use left or right to choose English or Spanish, then press Space or Z to select.", "menustate", SpeechPriority.Focus, 0f);
            }

            HandleLanguageInput(menu);
        }

        private static void LocateLanguageButtons(MainMenuScript menu)
        {
            _languageEnglish = null;
            _languageSpanish = null;
            if (menu.LanguageSelectionPage == null)
            {
                return;
            }
            foreach (Button button in menu.LanguageSelectionPage.GetComponentsInChildren<Button>(true))
            {
                Button.ButtonClickedEvent clicked = button.onClick;
                for (int i = 0; i < clicked.GetPersistentEventCount(); i++)
                {
                    string method = clicked.GetPersistentMethodName(i);
                    if (method == "SetLanguage_English")
                    {
                        _languageEnglish = button;
                    }
                    else if (method == "SetLanguage_Spanish")
                    {
                        _languageSpanish = button;
                    }
                }
            }
        }

        private static void SelectLanguage(Button button)
        {
            if (button != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(button.gameObject);
            }
        }

        private static void HandleLanguageInput(MainMenuScript menu)
        {
            bool arrow = UnityEngine.Input.GetKeyDown(KeyCode.LeftArrow)
                || UnityEngine.Input.GetKeyDown(KeyCode.RightArrow)
                || UnityEngine.Input.GetKeyDown(KeyCode.UpArrow)
                || UnityEngine.Input.GetKeyDown(KeyCode.DownArrow);

            if (arrow)
            {
                if (_languageEnglish != null && _languageSpanish != null)
                {
                    GameObject current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                    bool onEnglish = current == _languageEnglish.gameObject;
                    SelectLanguage(onEnglish ? _languageSpanish : _languageEnglish);
                }
            }

            bool confirm = UnityEngine.Input.GetKeyDown(KeyCode.Return)
                || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)
                || UnityEngine.Input.GetKeyDown(KeyCode.Space)
                || UnityEngine.Input.GetKeyDown(KeyCode.Z);
            if (confirm)
            {
                GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (_languageEnglish != null && selected == _languageEnglish.gameObject)
                {
                    ActivateLanguage(menu, _languageEnglish, true);
                    return;
                }
                if (_languageSpanish != null && selected == _languageSpanish.gameObject)
                {
                    ActivateLanguage(menu, _languageSpanish, false);
                    return;
                }
                if (_languageEnglish != null)
                {
                    ActivateLanguage(menu, _languageEnglish, true);
                }
            }
        }

        private static void ActivateLanguage(MainMenuScript menu, Button button, bool english)
        {
            try
            {
                if (english)
                {
                    menu.SetLanguage_English();
                }
                else
                {
                    menu.SetLanguage_Spanish();
                }
                Speaker.InterruptNow(english ? "English selected." : "Spanish selected.", "menustate");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("Language select failed: " + ex);
            }
        }

        private static void HandlePressAnyKey(MainMenuScript menu, bool visible, float now)
        {
            if (!visible)
            {
                _wasPressAnyKeyVisible = false;
                _scanWasActive = false;
                _pendingStart = false;
                return;
            }

            if (!_wasPressAnyKeyVisible)
            {
                _wasPressAnyKeyVisible = true;
                Speaker.Speak("Press any key to start.", "menustate", SpeechPriority.Focus, 0f);
            }

            if (_pendingStart)
            {
                _pendingStart = false;
                ForceStart(menu);
                return;
            }

            if (AnyKeyboardKeyDown())
            {
                _pendingStart = true;
                return;
            }

            if (InputManager.IsScanning)
            {
                _scanWasActive = true;
                return;
            }

            if (!_scanWasActive)
            {
                _scanWasActive = true;
                return;
            }

            if (now < _nextRestart)
            {
                return;
            }
            _nextRestart = now + 3f;

            try
            {
                menu.StartCoroutine(menu.CheckForPlayerOneInputDevice());
                MelonLogger.Msg("[MainMenuWatcher] Input scan expired; restarted press-any-key scan.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("MainMenuWatcher restart failed: " + ex);
            }
        }

        /// <summary>
        /// True if any physical keyboard key (including Space, arrows, F-keys;
        /// excluding mouse and joystick buttons) was pressed this frame.
        /// </summary>
        private static bool AnyKeyboardKeyDown()
        {
            if (_keyCodes == null)
            {
                _keyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
            }
            for (int i = 0; i < _keyCodes.Length; i++)
            {
                if (_keyCodes[i] < KeyCode.Mouse0 && UnityEngine.Input.GetKeyDown(_keyCodes[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Completes the press-any-key screen directly on a keyboard key press,
        /// instead of waiting for the game's 10-second device scan (which silently
        /// expires and leaves the screen unresponsive). Runs one frame after the
        /// key was pressed so the same key does not immediately activate a menu
        /// button on the newly shown main menu.
        /// </summary>
        private static void ForceStart(MainMenuScript menu)
        {
            try
            {
                if (GC.ins != null)
                {
                    GC.ins.PlayerInputDevices[0] = "Keyboard";
                }
                menu.ShowAppropriatePage();
                MelonLogger.Msg("[MainMenuWatcher] Press-any-key: keyboard key detected; started game.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("MainMenuWatcher force start failed: " + ex);
            }
        }
    }
}
