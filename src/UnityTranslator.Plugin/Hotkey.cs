using System;
using System.Reflection;
using UnityEngine;

namespace UnityTranslator
{
    /// <summary>
    /// Reads a configurable hotkey through the new Input System when the game uses it,
    /// and falls back to the legacy UnityEngine.Input API otherwise.
    /// </summary>
    internal static class Hotkey
    {
        private static string _keyName = "F5";
        private static KeyCode _legacyKey = KeyCode.F5;
        private static bool _disabled;
        private static bool _legacyUnavailable;

        private static bool _newInputReady;
        private static bool _newInputTypesMissing;
        private static bool _failureLogged;
        private static Type _keyboardType;
        private static Type _keyType;
        private static PropertyInfo _keyboardCurrent;
        private static PropertyInfo _keyIndexer;
        private static PropertyInfo _wasPressed;
        private static object _keyValue;

        internal static void Configure(string keyName)
        {
            _keyName = string.IsNullOrWhiteSpace(keyName) ? "F5" : keyName.Trim();
            _disabled = string.Equals(_keyName, "None", StringComparison.OrdinalIgnoreCase);
            if (!Enum.TryParse(_keyName, true, out _legacyKey)) _legacyKey = KeyCode.F5;

            _newInputReady = false;
            _newInputTypesMissing = false;
            _failureLogged = false;
            _keyboardType = null;
            _keyType = null;
            _keyboardCurrent = null;
            _keyIndexer = null;
            _wasPressed = null;
            _keyValue = null;
            _legacyUnavailable = false;
        }

        internal static bool PressedThisFrame()
        {
            if (_disabled) return false;
            if (TryNewInput()) return true;
            return TryLegacyInput();
        }

        private static bool TryNewInput()
        {
            if (!_newInputReady && !_newInputTypesMissing) InitializeNewInput();
            if (!_newInputReady) return false;

            try
            {
                object keyboard = _keyboardCurrent.GetValue(null, null);
                if (keyboard == null) return false;
                object control = _keyIndexer.GetValue(keyboard, new object[] { _keyValue });
                if (control == null) return false;
                return (bool)_wasPressed.GetValue(control, null);
            }
            catch (Exception ex)
            {
                _newInputReady = false;
                LogFailureOnce("new Input System read failed: " + ex.Message);
                return false;
            }
        }

        private static void InitializeNewInput()
        {
            try
            {
                _keyboardType = FindType("UnityEngine.InputSystem.Keyboard");
                _keyType = FindType("UnityEngine.InputSystem.Key");
                if (_keyboardType == null || _keyType == null)
                {
                    _newInputTypesMissing = true;
                    LogFailureOnce("new Input System types not loaded.");
                    return;
                }

                object keyValue;
                if (!TryParseKey(_keyType, _keyName, out keyValue))
                {
                    LogFailureOnce("key name '" + _keyName + "' is not valid for the new Input System.");
                    return;
                }
                _keyValue = keyValue;

                _keyboardCurrent = _keyboardType.GetProperty(
                    "current", BindingFlags.Public | BindingFlags.Static);
                if (_keyboardCurrent == null)
                {
                    LogFailureOnce("Keyboard.current property not found.");
                    return;
                }

                PropertyInfo[] properties = _keyboardType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < properties.Length; i++)
                {
                    ParameterInfo[] parameters = properties[i].GetIndexParameters();
                    if (parameters.Length != 1) continue;
                    if (!parameters[0].ParameterType.IsAssignableFrom(_keyType)) continue;
                    _keyIndexer = properties[i];
                    break;
                }
                if (_keyIndexer == null)
                {
                    LogFailureOnce("Keyboard key indexer not found.");
                    return;
                }

                object keyboard = _keyboardCurrent.GetValue(null, null);
                if (keyboard == null)
                {
                    // The Input System may not have created the keyboard device yet.
                    // Keep retrying silently; this is normal during startup.
                    return;
                }

                object control = _keyIndexer.GetValue(keyboard, new object[] { keyValue });
                if (control == null)
                {
                    LogFailureOnce("Key control is null.");
                    return;
                }

                _wasPressed = control.GetType().GetProperty("wasPressedThisFrame");
                if (_wasPressed == null)
                {
                    LogFailureOnce("wasPressedThisFrame property not found.");
                    return;
                }

                _newInputReady = true;
                TranslationPlugin.LogInfo("Hotkey " + _keyName + " is active (new Input System).");
            }
            catch (Exception ex)
            {
                _newInputReady = false;
                LogFailureOnce("new Input System setup failed: " + ex.Message);
            }
        }

        private static void LogFailureOnce(string message)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            TranslationPlugin.LogWarning("Hotkey: " + message + " Legacy input will still be tried.");
        }

        private static bool TryLegacyInput()
        {
            if (_legacyUnavailable) return false;
            try
            {
                return Input.GetKeyDown(_legacyKey);
            }
            catch (Exception ex)
            {
                // Some games use the new Input System exclusively; calling the
                // legacy API would throw every frame.
                _legacyUnavailable = true;
                if (!_newInputReady) LogFailureOnce("legacy Input unavailable: " + ex.Message);
                return false;
            }
        }

        private static bool TryParseKey(Type keyType, string name, out object value)
        {
            value = null;
            try
            {
                value = Enum.Parse(keyType, name, true);
                return true;
            }
            catch
            {
                // KeyCode and InputSystem Key names mostly match, but a few differ.
                string mapped;
                if (name.Equals("Return", StringComparison.OrdinalIgnoreCase)) mapped = "Enter";
                else if (name.Equals("Escape", StringComparison.OrdinalIgnoreCase)) mapped = "Escape";
                else mapped = name;

                try
                {
                    value = Enum.Parse(keyType, mapped, true);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static Type FindType(string fullName)
        {
            try
            {
                Type type = Type.GetType(fullName + ", Unity.InputSystem");
                if (type != null) return type;

                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    try
                    {
                        type = assemblies[i].GetType(fullName, false);
                        if (type != null) return type;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }
    }
}
