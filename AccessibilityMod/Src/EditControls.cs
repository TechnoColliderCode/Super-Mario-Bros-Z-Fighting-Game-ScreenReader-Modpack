using System;
using System.Reflection;
using MelonLoader;
using TeamUtility.IO;
using TeamUtility.IO.ZIG_Trinity;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SMBZG.Accessibility
{
    /// <summary>
    /// Makes the game's own "Edit Controls" / pause-menu "Rebind Keys" rows
    /// keyboard-accessible. The game only starts a rebind scan on mouse
    /// pointer-down; this module announces each focused row's action and current
    /// key, starts the scan when Enter or Space is pressed, and announces the
    /// new binding when the scan completes.
    /// </summary>
    internal static class EditControls
    {
        private static FieldInfo _configField;
        private static FieldInfo _axisField;
        private static FieldInfo _positiveField;
        private static FieldInfo _altField;
        private static RebindInput _editingRebind;
        private static bool _scanWasActive;
        private static float _nextTriggerTime;

        public static void OnSceneChanged()
        {
            _editingRebind = null;
            _scanWasActive = false;
        }

        public static bool IsRebind(GameObject obj)
        {
            return obj != null && obj.GetComponent<RebindInput>() != null;
        }

        /// <summary>Spoken label for a focused rebind row, e.g. "Rebind: Jump, currently X".</summary>
        public static string Label(GameObject obj)
        {
            try
            {
                if (obj == null)
                {
                    return null;
                }
                RebindInput rebind = obj.GetComponent<RebindInput>();
                if (rebind == null)
                {
                    return null;
                }
                return "Rebind: " + ActionLabel(rebind) + ", currently " + CurrentKeyName(rebind);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("EditControls: label failed: " + ex);
                return null;
            }
        }

        public static void Update()
        {
            try
            {
                UpdateCore();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("EditControls: update failed: " + ex);
            }
        }

        private static void UpdateCore()
        {
            if (_editingRebind != null)
            {
                bool scanning = InputManager.IsScanning;
                if (scanning)
                {
                    _scanWasActive = true;
                }
                else if (_scanWasActive)
                {
                    _scanWasActive = false;
                    RebindInput edited = _editingRebind;
                    _editingRebind = null;
                    Speaker.Speak("Rebind " + ActionLabel(edited) + " set to " + CurrentKeyName(edited) + ".", "rebind", SpeechPriority.Focus, 0f);
                }
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }
            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null)
            {
                return;
            }
            RebindInput rebind = selected.GetComponent<RebindInput>();
            if (rebind == null)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            bool confirm = InputManager.GetButtonDown("Submit")
                || UnityEngine.Input.GetKeyDown(KeyCode.Return)
                || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)
                || UnityEngine.Input.GetKeyDown(KeyCode.Space);
            if (!confirm || now < _nextTriggerTime)
            {
                return;
            }
            _nextTriggerTime = now + 0.3f;
            StartScan(rebind);
        }

        private static void StartScan(RebindInput rebind)
        {
            _editingRebind = rebind;
            _scanWasActive = InputManager.IsScanning;
            Speaker.InterruptNow("Press the new key for " + ActionLabel(rebind) + ", or Escape to cancel.", "rebind");
            try
            {
                rebind.StartCoroutine(rebind.StartInputScanDelayed());
            }
            catch (Exception ex)
            {
                MelonLogger.Error("EditControls: failed to start rebind scan: " + ex);
            }
        }

        private static bool _reflectionReady;

        private static string ActionLabel(RebindInput rebind)
        {
            if (!EnsureReflection())
            {
                return "action";
            }
            string axis = (string)_axisField.GetValue(rebind);
            bool positive = (bool)_positiveField.GetValue(rebind);
            bool alt = (bool)_altField.GetValue(rebind);
            return Remapper.ActionLabel(axis ?? string.Empty, !positive, alt);
        }

        private static string CurrentKeyName(RebindInput rebind)
        {
            if (!EnsureReflection())
            {
                return "unknown";
            }
            string config = (string)_configField.GetValue(rebind);
            string axis = (string)_axisField.GetValue(rebind);
            bool positive = (bool)_positiveField.GetValue(rebind);
            bool alt = (bool)_altField.GetValue(rebind);
            AxisConfiguration axisConfig = config != null && axis != null
                ? InputManager.GetAxisConfiguration(config, axis)
                : null;
            if (axisConfig == null)
            {
                return "not bound";
            }
            KeyCode key = positive
                ? (alt ? axisConfig.altPositive : axisConfig.positive)
                : (alt ? axisConfig.altNegative : axisConfig.negative);
            return key == KeyCode.None ? "not bound" : Remapper.KeyName(key);
        }

        private static bool EnsureReflection()
        {
            if (_reflectionReady)
            {
                return true;
            }
            try
            {
                Type type = typeof(RebindInput);
                _configField = type.GetField("_inputConfigName", BindingFlags.NonPublic | BindingFlags.Instance);
                _axisField = type.GetField("_axisConfigName", BindingFlags.NonPublic | BindingFlags.Instance);
                _positiveField = type.GetField("_changePositiveKey", BindingFlags.NonPublic | BindingFlags.Instance);
                _altField = type.GetField("_changeAltKey", BindingFlags.NonPublic | BindingFlags.Instance);
                _reflectionReady = _configField != null
                    && _axisField != null
                    && _positiveField != null
                    && _altField != null;
                if (!_reflectionReady)
                {
                    MelonLogger.Error("EditControls: rebind reflection failed; keyboard rebind of game rows disabled.");
                }
                return _reflectionReady;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("EditControls: reflection error: " + ex);
                return false;
            }
        }
    }
}
