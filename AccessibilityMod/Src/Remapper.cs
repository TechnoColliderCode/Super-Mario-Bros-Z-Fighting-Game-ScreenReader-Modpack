using System;
using System.Collections.Generic;
using TeamUtility.IO;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SMBZG.Accessibility
{
    /// <summary>
    /// Keyboard-only input remapping flow. Opens with a hotkey, walks the user through
    /// each action with speech prompts, and writes the changes to the game's input
    /// configuration via TeamUtility.IO.InputManager.
    /// </summary>
    internal static class Remapper
    {
        private sealed class RemapAction
        {
            public string DisplayName;
            public string AxisName;
            public bool IsNegative;

            public RemapAction(string displayName, string axisName, bool isNegative)
            {
                DisplayName = displayName;
                AxisName = axisName;
                IsNegative = isNegative;
            }
        }

        private enum State
        {
            Idle,
            SelectingAction,
            CapturingKey
        }

        private static readonly List<RemapAction> Actions = new List<RemapAction>
        {
            new RemapAction("Left", "Horizontal", true),
            new RemapAction("Right", "Horizontal", false),
            new RemapAction("Up", "Vertical", true),
            new RemapAction("Down", "Vertical", false),
            new RemapAction("Attack", "Attack", false),
            new RemapAction("Defend", "Defend", false),
            new RemapAction("Jump", "Jump", false),
            new RemapAction("Pursue", "Pursue", false),
            new RemapAction("Dash", "Dash", false),
            new RemapAction("Z Trigger", "ZTrigger", false),
            new RemapAction("Z Attack", "ZAttack", false),
            new RemapAction("Taunt", "Taunt", false),
            new RemapAction("Submit", "Submit", false),
            new RemapAction("Cancel", "Cancel", false)
        };

        private static State _state = State.Idle;
        private static PlayerID _player;
        private static int _actionIndex;
        private static bool _savedNavState;
        private static KeyCode[] _keys;

        public static bool IsActive { get { return _state != State.Idle; } }

        /// <summary>
        /// Force-exits remap mode when the scene changes so the flow can never be
        /// left active (which would disable menu narration, arrow navigation and
        /// swallow all keys in the new scene).
        /// </summary>
        public static void OnSceneChanged()
        {
            if (_state == State.Idle)
            {
                return;
            }
            _state = State.Idle;
            UiNarrator.IsEnabled = true;
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.sendNavigationEvents = true;
            }
            MelonLoader.MelonLogger.Msg("[Remapper] Force-exited remap mode on scene change.");
        }

        public static void Toggle(PlayerID playerID)
        {
            if (_state != State.Idle)
            {
                Exit();
                return;
            }
            StartFlow(playerID);
        }

        public static void Update()
        {
            if (_state == State.Idle)
            {
                return;
            }
            if (_state == State.SelectingAction)
            {
                HandleSelecting();
            }
            else if (_state == State.CapturingKey)
            {
                HandleCapturing();
            }
        }

        private static void StartFlow(PlayerID playerID)
        {
            _player = playerID;
            _actionIndex = 0;
            _state = State.SelectingAction;
            UiNarrator.IsEnabled = false;

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                _savedNavState = eventSystem.sendNavigationEvents;
                eventSystem.sendNavigationEvents = false;
            }

            string playerName = playerID == PlayerID.One ? "Player 1" : "Player 2";
            Speaker.InterruptNow("Remap " + playerName + ". Use up and down arrows to choose an action, Enter to select, Escape to finish.", "remap");
            AnnounceCurrentAction();
        }

        private static void Exit()
        {
            _state = State.Idle;
            UiNarrator.IsEnabled = true;
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.sendNavigationEvents = _savedNavState;
            }
            Speaker.InterruptNow("Remapping finished.", "remap");
        }

        /// <summary>True when the Control modifier is held (required to open remap mode).</summary>
        public static bool ModifierHeld
        {
            get { return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl); }
        }

        private static void HandleSelecting()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Exit();
                return;
            }
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            {
                _actionIndex = (_actionIndex + Actions.Count - 1) % Actions.Count;
                AnnounceCurrentAction();
                return;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            {
                _actionIndex = (_actionIndex + 1) % Actions.Count;
                AnnounceCurrentAction();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                RemapAction action = Actions[_actionIndex];
                Speaker.InterruptNow("Press the new key for " + action.DisplayName + ", or Escape to cancel.", "remap");
                _state = State.CapturingKey;
            }
        }

        private static void HandleCapturing()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Speaker.InterruptNow("Canceled. Choose another action, or press Escape to finish.", "remap");
                _state = State.SelectingAction;
                return;
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                RemapAction clearAction = Actions[_actionIndex];
                ApplyBinding(_player, clearAction, KeyCode.None);
                Speaker.InterruptNow(clearAction.DisplayName + " cleared. Choose another action, or press Escape to finish.", "remap");
                _state = State.SelectingAction;
                return;
            }
            KeyCode? key = GetPressedKey();
            if (!key.HasValue)
            {
                return;
            }
            RemapAction action = Actions[_actionIndex];
            ApplyBinding(_player, action, key.Value);
            Speaker.InterruptNow(action.DisplayName + " set to " + KeyName(key.Value) + ". Choose another action, or press Escape to finish.", "remap");
            _state = State.SelectingAction;
        }

        private static void ApplyBinding(PlayerID playerID, RemapAction action, KeyCode key)
        {
            InputConfiguration config = InputManager.GetInputConfiguration(playerID);
            AxisConfiguration axis = InputManager.GetAxisConfiguration(playerID, action.AxisName);
            if (config == null || axis == null)
            {
                Speaker.Speak("Could not find input configuration for action " + action.DisplayName + ".", "remap", SpeechPriority.Error, 0f);
                return;
            }
            if (action.IsNegative)
            {
                axis.negative = key;
            }
            else
            {
                axis.positive = key;
            }
            try
            {
                InputManager.SetConfigurationDirty(config.name);
                InputManager.Save();
            }
            catch (Exception ex)
            {
                Speaker.Speak("Failed to save input changes.", "remap", SpeechPriority.Error, 0f);
                MelonLoader.MelonLogger.Error("SMBZG Accessibility remap save failed: " + ex);
            }
        }

        private static void AnnounceCurrentAction()
        {
            RemapAction action = Actions[_actionIndex];
            AxisConfiguration axis = InputManager.GetAxisConfiguration(_player, action.AxisName);
            string bound = "not bound";
            if (axis != null)
            {
                KeyCode key = action.IsNegative ? axis.negative : axis.positive;
                if (key != KeyCode.None)
                {
                    bound = KeyName(key);
                }
            }
            Speaker.Speak(action.DisplayName + ", currently " + bound + ".", "remap", SpeechPriority.Remap, 0.2f);
        }

        /// <summary>Announces the full current binding summary for a player (help hotkey).</summary>
        public static void AnnounceBindings(PlayerID playerID)
        {
            string playerName = playerID == PlayerID.One ? "Player 1" : "Player 2";
            string result = playerName + ": ";
            for (int i = 0; i < Actions.Count; i++)
            {
                RemapAction action = Actions[i];
                AxisConfiguration axis = InputManager.GetAxisConfiguration(playerID, action.AxisName);
                string bound = "none";
                if (axis != null)
                {
                    KeyCode key = action.IsNegative ? axis.negative : axis.positive;
                    if (key != KeyCode.None)
                    {
                        bound = KeyName(key);
                    }
                }
                result += action.DisplayName + " " + bound + ". ";
            }
            Speaker.Speak(result, "help", SpeechPriority.Status, 0f);
        }

        // ---- Helpers ----

        private static KeyCode? GetPressedKey()
        {
            if (_keys == null)
            {
                _keys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
            }
            for (int i = 0; i < _keys.Length; i++)
            {
                KeyCode key = _keys[i];
                if (IsBindableKey(key) && Input.GetKeyDown(key))
                {
                    return key;
                }
            }
            return null;
        }

        /// <summary>
        /// True for any key that may be assigned as a binding. Works from physical
        /// key position (Unity KeyCode), so it is the same on every keyboard layout
        /// (Spanish, Canadian, French AZERTY, etc.). Only flow-control keys and
        /// modifiers are excluded; Space and Tab are bindable.
        /// </summary>
        private static bool IsBindableKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None:
                case KeyCode.Escape:
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Backspace:
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                case KeyCode.LeftWindows:
                case KeyCode.RightWindows:
                case KeyCode.LeftCommand:
                case KeyCode.RightCommand:
                    return false;
            }
            if (key >= KeyCode.JoystickButton0)
            {
                return false;
            }
            if (key < KeyCode.Exclaim)
            {
                return key == KeyCode.Space || key == KeyCode.Tab;
            }
            return true;
        }

        public static string KeyName(KeyCode key)        {
            switch (key)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    return "Enter";
                case KeyCode.Space:
                    return "Space bar";
                case KeyCode.Escape:
                    return "Escape";
                case KeyCode.UpArrow:
                    return "Up arrow";
                case KeyCode.DownArrow:
                    return "Down arrow";
                case KeyCode.LeftArrow:
                    return "Left arrow";
                case KeyCode.RightArrow:
                    return "Right arrow";
                case KeyCode.LeftShift:
                    return "Left shift";
                case KeyCode.RightShift:
                    return "Right shift";
                case KeyCode.LeftControl:
                    return "Left control";
                case KeyCode.RightControl:
                    return "Right control";
                case KeyCode.LeftAlt:
                    return "Left alt";
                case KeyCode.RightAlt:
                    return "Right alt";
                case KeyCode.LeftWindows:
                    return "Left windows";
                case KeyCode.RightWindows:
                    return "Right windows";
                case KeyCode.Backspace:
                    return "Backspace";
                case KeyCode.Tab:
                    return "Tab";
                case KeyCode.CapsLock:
                    return "Caps lock";
                case KeyCode.Quote:
                    return "Apostrophe";
                case KeyCode.Semicolon:
                    return "Semicolon";
                case KeyCode.Comma:
                    return "Comma";
                case KeyCode.Period:
                    return "Period";
                case KeyCode.Slash:
                    return "Slash";
                case KeyCode.BackQuote:
                    return "Tilde";
                case KeyCode.LeftBracket:
                    return "Left bracket";
                case KeyCode.RightBracket:
                    return "Right bracket";
                case KeyCode.Backslash:
                    return "Backslash";
                case KeyCode.Minus:
                    return "Minus";
                case KeyCode.Equals:
                    return "Equals";
                case KeyCode.Delete:
                    return "Delete";
                case KeyCode.Insert:
                    return "Insert";
                case KeyCode.Home:
                    return "Home";
                case KeyCode.End:
                    return "End";
                case KeyCode.PageUp:
                    return "Page up";
                case KeyCode.PageDown:
                    return "Page down";
                case KeyCode.Numlock:
                    return "Num lock";
                case KeyCode.ScrollLock:
                    return "Scroll lock";
                case KeyCode.Pause:
                    return "Pause";
                default:
                    return SplitCamelCase(key.ToString());
            }
        }

        /// <summary>Friendly label for an axis slot, e.g. "Right", "Jump", "Alternate Right".</summary>
        public static string ActionLabel(string axisName, bool isNegative, bool alternate)
        {
            for (int i = 0; i < Actions.Count; i++)
            {
                RemapAction action = Actions[i];
                if (action.AxisName == axisName && action.IsNegative == isNegative)
                {
                    return alternate ? "Alternate " + action.DisplayName : action.DisplayName;
                }
            }
            string baseName = FriendlyAxisName(axisName);
            if (alternate)
            {
                baseName += isNegative ? " negative alternate" : " positive alternate";
            }
            else
            {
                baseName += isNegative ? " negative" : " positive";
            }
            return baseName;
        }

        public static string FriendlyAxisName(string axisName)
        {
            for (int i = 0; i < Actions.Count; i++)
            {
                if (Actions[i].AxisName == axisName)
                {
                    return Actions[i].DisplayName;
                }
            }
            return SplitCamelCase(axisName);
        }

        public static string SplitCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }
            if (value.Length >= 5 && value.StartsWith("Alpha"))
            {
                return value.Substring(5);
            }
            if (value.Length >= 6 && value.StartsWith("Keypad"))
            {
                return "Numpad " + value.Substring(6);
            }
            var chars = new System.Text.StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (i > 0 && char.IsUpper(c))
                {
                    chars.Append(' ');
                }
                chars.Append(c);
            }
            return chars.ToString();
        }
    }
}
