using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityTranslator
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class TranslationPlugin : BaseUnityPlugin
    {
        public const string Guid = "ru.unitytranslator";
        public const string Name = "Universal Unity Translator";
        public const string Version = "1.0.0";

        internal static TranslationPlugin Instance;
        internal static TranslationService Service;
        internal static string TargetLanguage = "en";
        internal static bool Ready;
        internal static bool LogTranslations;

        private static BepInEx.Logging.ManualLogSource _log;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<string> _targetLanguage;
        private ConfigEntry<float> _scanInterval;
        private ConfigEntry<string> _provider;
        private ConfigEntry<string> _libreUrl;
        private ConfigEntry<string> _libreKey;
        private ConfigEntry<int> _requestDelay;
        private ConfigEntry<int> _workerThreads;
        private ConfigEntry<int> _stableTextDelay;
        private ConfigEntry<int> _revealGraceMs;
        private ConfigEntry<string> _reloadKey;
        private ConfigEntry<int> _minLetters;
        private ConfigEntry<string> _excludedNames;
        private ConfigEntry<bool> _fontFallback;
        private ConfigEntry<bool> _logTranslations;
        private ConfigEntry<int> _maxTextLength;

        private Harmony _harmony;
        private float _nextScan;
        private float _nextSave;
        private bool _shutdown;
        private bool _updateErrorLogged;

        private void Awake()
        {
            Instance = this;
            _log = Logger;

            _enabled = Config.Bind("General", "Enabled", true,
                "Enable automatic translation.");
            _targetLanguage = Config.Bind("General", "TargetLanguage", "auto",
                "Target language. 'auto' uses the Windows system UI language.");
            _scanInterval = Config.Bind("General", "ScanIntervalSeconds", 3f,
                new ConfigDescription("How often to scan the scene for new UI text (seconds).",
                    new AcceptableValueRange<float>(1f, 60f)));
            _provider = Config.Bind("Translation", "Provider", "google",
                "Translation provider: 'google' or 'libretranslate'.");
            _libreUrl = Config.Bind("Translation", "LibreTranslateUrl", "",
                "LibreTranslate server URL, for example https://translate.example.com");
            _libreKey = Config.Bind("Translation", "LibreTranslateApiKey", "",
                "Optional LibreTranslate API key.");
            _requestDelay = Config.Bind("Translation", "RequestDelayMs", 120,
                new ConfigDescription("Minimum delay between translation requests in milliseconds.",
                    new AcceptableValueRange<int>(0, 5000)));
            _workerThreads = Config.Bind("Translation", "WorkerThreads", 2,
                new ConfigDescription("Number of background translation workers.",
                    new AcceptableValueRange<int>(1, 8)));
            _stableTextDelay = Config.Bind("Text", "StableTextDelayMs", 400,
                new ConfigDescription(
                    "Wait this long after the last text change before translating. " +
                    "Prevents translating typewriter/marquee fragments.",
                    new AcceptableValueRange<int>(0, 5000)));
            _revealGraceMs = Config.Bind("Text", "RevealGraceMs", 1500,
                new ConfigDescription(
                    "Extra wait after a typewriter/reveal animation finishes before replacing text. " +
                    "Increase this if a game still freezes on animated text.",
                    new AcceptableValueRange<int>(0, 10000)));
            _reloadKey = Config.Bind("Text", "ReloadTextKey", "F5",
                "Hotkey that restores and retranslates all visible text. " +
                "Use a UnityEngine.KeyCode name such as F5, F8, R, Home, or 'None' to disable.");
            _minLetters = Config.Bind("Text", "MinLetters", 2,
                new ConfigDescription("Skip strings with fewer visible letters.",
                    new AcceptableValueRange<int>(1, 100)));
            _excludedNames = Config.Bind("Text", "ExcludedNames", "",
                "Comma- or semicolon-separated scene, GameObject or component name fragments " +
                "whose text is never translated. Example: Slides,Typewriter,EndingScreen");
            _fontFallback = Config.Bind("Fonts", "EnableFontFallback", true,
                "Automatically add a Windows font when the game font lacks translated characters.");
            _logTranslations = Config.Bind("Diagnostics", "LogTranslations", false,
                "Write source and translated text to the BepInEx log.");
            _maxTextLength = Config.Bind("Diagnostics", "MaxTextLength", 10000,
                "Do not translate single strings longer than this (characters).");

            LogTranslations = _logTranslations.Value;
            TextUtil.MaxTextLength = Math.Max(100, _maxTextLength.Value);
            TextUtil.MinLetters = Math.Max(1, _minLetters.Value);
            TextHooks.StableDelaySeconds = Math.Max(0, _stableTextDelay.Value) / 1000f;
            TextHooks.RevealGraceSeconds = Math.Max(0, _revealGraceMs.Value) / 1000f;
            TextHooks.ExcludedTokens = ParseTokens(_excludedNames.Value);
            Hotkey.Configure(_reloadKey.Value);

            if (!_enabled.Value)
            {
                LogInfo("Plugin is disabled in the config.");
                return;
            }

            string systemLanguage = LanguageUtil.GetSystemLanguage();
            TargetLanguage = LanguageUtil.ResolveTargetLanguage(_targetLanguage.Value);
            FontSupport.Initialize(TargetLanguage, _fontFallback.Value);

            LogInfo("System language: " + systemLanguage + " (" + LanguageUtil.FriendlyName(systemLanguage) + ")");
            LogInfo("Translation target: " + TargetLanguage + " (" + LanguageUtil.FriendlyName(TargetLanguage) + ")");
            LogInfo("Text delay: " + _stableTextDelay.Value + " ms, reveal grace: " +
                    _revealGraceMs.Value + " ms, minimum letters: " + TextUtil.MinLetters +
                    ", exclusions: " + TextHooks.ExcludedTokens.Length +
                    ", reload key: " + _reloadKey.Value);

            ITranslator translator;
            if (string.Equals(_provider.Value, "libretranslate", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(_libreUrl.Value))
            {
                translator = new LibreTranslator(_libreUrl.Value, _libreKey.Value);
            }
            else
            {
                translator = new GoogleTranslator();
                if (string.Equals(_provider.Value, "libretranslate", StringComparison.OrdinalIgnoreCase))
                    LogWarning("LibreTranslate selected, but LibreTranslateUrl is empty. Falling back to Google.");
            }

            string cacheDirectory = Path.Combine(Paths.ConfigPath, "UnityTranslator");
            string safeTarget = TargetLanguage.Replace('/', '_').Replace('\\', '_');
            string cachePath = Path.Combine(cacheDirectory, "cache_" + safeTarget + ".tsv");

            Service = new TranslationService(TargetLanguage, cachePath, translator,
                                             _workerThreads.Value, _requestDelay.Value);
            Service.Start();

            _harmony = new Harmony(Guid);
            int patched = Patcher.Install(_harmony);
            LogInfo("Patched " + patched + " text method(s).");
            if (patched == 0)
                LogWarning("No text methods were patched. Scene scanning will still run.");

            SceneManager.sceneLoaded += OnSceneLoaded;
            Ready = true;
            StartCoroutine(StartupScan());
            _nextScan = Time.unscaledTime + _scanInterval.Value;
            _nextSave = Time.unscaledTime + 30f;

            LogInfo("Ready. Provider: " + translator.Name + ", cache: " + cachePath);
        }

        private IEnumerator StartupScan()
        {
            yield return new WaitForSecondsRealtime(1f);
            if (Ready) TextHooks.ScanAll();
            yield return new WaitForSecondsRealtime(2f);
            if (Ready) TextHooks.ScanAll();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Ready) return;
            StartCoroutine(ScanSceneLater(0.4f));
            StartCoroutine(ScanSceneLater(2f));
        }

        private IEnumerator ScanSceneLater(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (Ready) TextHooks.ScanAll();
        }

        private void Update()
        {
            if (!Ready || Service == null) return;
            try
            {
                Service.Pump();
                if (Hotkey.PressedThisFrame()) TextHooks.ReloadText();
                TextHooks.ProcessStableText();

                float now = Time.unscaledTime;
                if (now >= _nextScan)
                {
                    _nextScan = now + Math.Max(1f, _scanInterval.Value);
                    TextHooks.ScanAll();
                }
                if (now >= _nextSave)
                {
                    _nextSave = now + 30f;
                    Service.SaveCacheIfDirty();
                }
            }
            catch (Exception ex)
            {
                if (!_updateErrorLogged)
                {
                    _updateErrorLogged = true;
                    LogError("Update loop error: " + ex);
                }
            }
        }

        private static string[] ParseTokens(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new string[0];
            string[] raw = value.Split(new char[] { ',', ';', '|', '\r', '\n' },
                                       StringSplitOptions.RemoveEmptyEntries);
            List<string> tokens = new List<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                string token = raw[i].Trim();
                if (token.Length == 0) continue;
                bool duplicate = false;
                for (int j = 0; j < tokens.Count; j++)
                {
                    if (string.Equals(tokens[j], token, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) tokens.Add(token);
            }
            return tokens.ToArray();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;
            Ready = false;
            try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            if (Service != null)
            {
                try { Service.Shutdown(); }
                catch (Exception ex) { LogWarning("Could not stop translator: " + ex.Message); }
                Service = null;
            }
            if (_harmony != null)
            {
                try { _harmony.UnpatchSelf(); } catch { }
                _harmony = null;
            }
            LogInfo("Translator stopped.");
        }

        internal static void LogInfo(string message)
        {
            if (_log != null) _log.LogInfo(message);
        }

        internal static void LogWarning(string message)
        {
            if (_log != null) _log.LogWarning(message);
        }

        internal static void LogError(string message)
        {
            if (_log != null) _log.LogError(message);
        }
    }
}
