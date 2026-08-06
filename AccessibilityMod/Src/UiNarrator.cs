using SMBZG.CharacterSelect;
using TMPro;
using TeamUtility.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SMBZG.Accessibility
{
    /// <summary>
    /// Narrates menu navigation by watching the EventSystem's currently selected
    /// GameObject and reading the label of the focused control.
    /// </summary>
    internal static class UiNarrator
    {
        private static GameObject _lastSelected;
        private static string _lastAnnouncedLabel = string.Empty;
        private static bool _enabled = true;
        private static string _currentScene = string.Empty;

        private static float _nextPressAnnounceTime;

        public static bool IsEnabled { get { return _enabled; } set { _enabled = value; } }

        public static void OnSceneChanged(string sceneName)
        {
            _currentScene = sceneName ?? string.Empty;
            _lastSelected = null;
            _lastAnnouncedLabel = string.Empty;
        }

        public static void Update()
        {
            if (!_enabled || !Settings.AnnounceFocus)
            {
                return;
            }
            EditControls.Update();
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }
            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null || !selected.activeInHierarchy)
            {
                _lastSelected = null;
                EnsureSelection();
                return;
            }
            if (selected == _lastSelected)
            {
                AnnouncePressIfNeeded(selected);
                return;
            }
            _lastSelected = selected;

            string label = ExtractLabel(selected);
            if (string.IsNullOrEmpty(label) || label == _lastAnnouncedLabel)
            {
                return;
            }
            _lastAnnouncedLabel = label;
            Speaker.InterruptNow(label, "focus");
        }

        private static void AnnouncePressIfNeeded(GameObject selected)
        {
            if (EditControls.IsRebind(selected))
            {
                return;
            }
            bool submit = InputManager.GetButtonDown("Submit");
            bool cancel = InputManager.GetButtonDown("Cancel");
            if (!submit && !cancel)
            {
                return;
            }
            if (Time.realtimeSinceStartup < _nextPressAnnounceTime)
            {
                return;
            }
            _nextPressAnnounceTime = Time.realtimeSinceStartup + 0.35f;

            string label = ExtractLabel(selected);
            if (string.IsNullOrEmpty(label))
            {
                return;
            }
            if (submit)
            {
                Speaker.Speak(cancel ? label + " pressed" : label, "focuspress", SpeechPriority.Focus, 0.3f);
            }
        }

        /// <summary>
        /// If the EventSystem has no live selection, pick the first interactive
        /// selectable in menu scenes so arrow/Enter navigation always works. The
        /// game's own Select() can fail or be skipped after the press-any-key
        /// transition, which otherwise leaves the menu unresponsive.
        /// </summary>
        private static void EnsureSelection()
        {
            if (!IsMenuScene)
            {
                return;
            }
            System.Collections.Generic.List<Selectable> all = Selectable.allSelectables;
            for (int i = 0; i < all.Count; i++)
            {
                Selectable selectable = all[i];
                if (selectable != null && selectable.gameObject != null
                    && selectable.gameObject.activeInHierarchy && selectable.IsInteractable())
                {
                    selectable.Select();
                    return;
                }
            }
        }

        private static bool IsMenuScene
        {
            get
            {
                switch (_currentScene)
                {
                    case SceneConstants.MainMenu:
                    case SceneConstants.CharacterSelect:
                    case SceneConstants.CharacterSelect_Arcade:
                    case SceneConstants.BattleResults:
                    case SceneConstants.BattleResults_ArcadeReview:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>Builds a spoken label for a focused UI element.</summary>
        public static string ExtractLabel(GameObject obj)
        {
            if (obj == null)
            {
                return null;
            }
            string rebindLabel = EditControls.Label(obj);
            if (rebindLabel != null)
            {
                return rebindLabel;
            }
            CharacterPortrait portrait = obj.GetComponent<CharacterPortrait>();
            if (portrait != null && portrait.Data != null)
            {
                string characterName = CharacterName(portrait.Data.Character);
                if (!string.IsNullOrEmpty(characterName))
                {
                    return characterName + ", Button";
                }
            }
            TMP_Dropdown tmpDropdown = obj.GetComponent<TMP_Dropdown>();
            if (tmpDropdown != null)
            {
                string cap = tmpDropdown.captionText != null ? tmpDropdown.captionText.text : null;
                if (string.IsNullOrEmpty(cap))
                {
                    cap = tmpDropdown.value >= 0 && tmpDropdown.value < tmpDropdown.options.Count
                        ? tmpDropdown.options[tmpDropdown.value].text
                        : null;
                }
                return "Combo box: " + Clean(cap);
            }
            Dropdown dropdown = obj.GetComponent<Dropdown>();
            if (dropdown != null)
            {
                return "Combo box: " + Clean(dropdown.captionText != null ? dropdown.captionText.text : null);
            }
            TMP_InputField tmpInput = obj.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                return Clean(tmpInput.text);
            }
            InputField input = obj.GetComponent<InputField>();
            if (input != null)
            {
                return Clean(input.text);
            }
            Toggle toggle = obj.GetComponent<Toggle>();
            if (toggle != null)
            {
                string label = Clean(GetFirstText(obj));
                return label + (toggle.isOn ? ", on" : ", off");
            }
            Slider slider = obj.GetComponent<Slider>();
            if (slider != null)
            {
                string label = Clean(GetFirstText(obj));
                return label + ", " + slider.value.ToString("0");
            }

            string text = Clean(GetFirstText(obj));
            if (!string.IsNullOrEmpty(text))
            {
                if (obj.GetComponent<Button>() != null)
                {
                    return text + ", Button";
                }
                return text;
            }
            return null;
        }

        private static string GetFirstText(GameObject obj)
        {
            TMP_Text tmp = obj.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null && !string.IsNullOrEmpty(tmp.text))
            {
                return tmp.text;
            }
            Text text = obj.GetComponentInChildren<Text>(true);
            if (text != null && !string.IsNullOrEmpty(text.text))
            {
                return text.text;
            }
            return null;
        }

        private static string Clean(string value)
        {
            if (value == null)
            {
                return null;
            }
            string trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }
            return trimmed;
        }

        private static string CharacterName(BattleCache.CharacterEnum character)
        {
            switch (character)
            {
                case BattleCache.CharacterEnum.Mario: return "Mario";
                case BattleCache.CharacterEnum.Sonic: return "Sonic the Hedgehog";
                case BattleCache.CharacterEnum.KoopaBros: return "Koopa Bros";
                case BattleCache.CharacterEnum.Shadow: return "Shadow";
                case BattleCache.CharacterEnum.Yoshi: return "Yoshi";
                case BattleCache.CharacterEnum.MechaSonic: return "Mecha Sonic";
                case BattleCache.CharacterEnum.AxemRangersX: return "Axem Rangers X";
                case BattleCache.CharacterEnum.Luigi: return "Luigi";
                case BattleCache.CharacterEnum.CapedLuigi: return "Caped Luigi";
                case BattleCache.CharacterEnum.FireMario: return "Fire Mario";
                case BattleCache.CharacterEnum.Basilisx: return "Basilisx";
                case BattleCache.CharacterEnum.Goomba: return "Goomba";
                case BattleCache.CharacterEnum.SemiSuperMechaSonic_Goomba: return "Semi Super Mecha Sonic Goomba";
                default: return null;
            }
        }
    }
}
