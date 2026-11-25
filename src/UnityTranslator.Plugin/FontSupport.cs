using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnityTranslator
{
    /// <summary>
    /// Font fallback for legacy uGUI Text and classic TextMesh. This class deliberately
    /// contains no TextMeshPro types so it can load in games built without the TMP package.
    /// </summary>
    internal static class FontSupport
    {
        private static bool _enabled;
        private static string _targetLanguage;
        private static string[] _fontNames = new string[0];

        private static Font _legacyFallback;
        private static bool _legacyFallbackTried;
        private static readonly HashSet<string> _warnings = new HashSet<string>(StringComparer.Ordinal);

        public static void Initialize(string targetLanguage, bool enabled)
        {
            _targetLanguage = targetLanguage;
            _enabled = enabled;
            _fontNames = GetFontNames(targetLanguage);
        }

        internal static bool Enabled { get { return _enabled; } }
        internal static string TargetLanguage { get { return _targetLanguage; } }
        internal static string[] FontNames { get { return _fontNames; } }

        private static string[] GetFontNames(string targetLanguage)
        {
            List<string> names = new List<string>();
            string target = (targetLanguage ?? string.Empty).ToLowerInvariant();
            string baseCode = target;
            int dash = target.IndexOf('-');
            if (dash > 0) baseCode = target.Substring(0, dash);

            switch (baseCode)
            {
                case "ru": case "uk": case "be": case "bg": case "sr": case "mk": case "kk":
                    names.Add("Segoe UI"); names.Add("Arial"); names.Add("Tahoma");
                    names.Add("Calibri"); names.Add("Verdana");
                    break;
                case "zh":
                    if (target.Contains("tw") || target.Contains("hk") || target.Contains("mo"))
                    {
                        names.Add("Microsoft JhengHei UI"); names.Add("Microsoft JhengHei");
                        names.Add("MingLiU"); names.Add("PMingLiU");
                        names.Add("Noto Sans CJK TC"); names.Add("Noto Sans TC");
                    }
                    else
                    {
                        names.Add("Microsoft YaHei UI"); names.Add("Microsoft YaHei");
                        names.Add("SimSun"); names.Add("SimHei");
                        names.Add("Noto Sans CJK SC"); names.Add("Noto Sans SC");
                    }
                    names.Add("Segoe UI");
                    break;
                case "ja":
                    names.Add("Yu Gothic UI"); names.Add("Yu Gothic"); names.Add("Meiryo UI");
                    names.Add("Meiryo"); names.Add("MS Gothic"); names.Add("Noto Sans CJK JP");
                    names.Add("Segoe UI");
                    break;
                case "ko":
                    names.Add("Malgun Gothic"); names.Add("Gulim"); names.Add("Dotum");
                    names.Add("Noto Sans CJK KR"); names.Add("Segoe UI");
                    break;
                case "ar": case "fa": case "ur":
                    names.Add("Segoe UI"); names.Add("Tahoma"); names.Add("Arial");
                    names.Add("Noto Naskh Arabic");
                    break;
                case "he":
                    names.Add("Segoe UI"); names.Add("Arial"); names.Add("David");
                    names.Add("Noto Sans Hebrew");
                    break;
                case "th":
                    names.Add("Leelawadee UI"); names.Add("Tahoma"); names.Add("Segoe UI");
                    names.Add("Noto Sans Thai");
                    break;
                case "hi": case "mr": case "ne":
                    names.Add("Nirmala UI"); names.Add("Mangal"); names.Add("Segoe UI");
                    names.Add("Noto Sans Devanagari");
                    break;
                case "el":
                    names.Add("Segoe UI"); names.Add("Arial"); names.Add("Tahoma");
                    break;
                case "ka": case "hy":
                    names.Add("Sylfaen"); names.Add("Segoe UI"); names.Add("Arial");
                    break;
                default:
                    names.Add("Segoe UI"); names.Add("Arial"); names.Add("Tahoma");
                    break;
            }

            names.Add("Arial Unicode MS");
            return names.ToArray();
        }

        public static void EnsureLegacy(Text text, string translated)
        {
            if (!_enabled || text == null) return;
            Font font;
            try { font = text.font; }
            catch { font = null; }

            if (font != null &&
                !TextUtil.NeedsGlyphs(translated, delegate (char c) { return LegacyHasGlyph(font, c); }))
                return;

            Font fallback = GetOrCreateLegacyFallback();
            if (fallback == null || fallback == font) return;
            try
            {
                text.font = fallback;
                TranslationPlugin.LogInfo("Switched legacy UI Text to fallback font: " + fallback.name);
            }
            catch (Exception ex)
            {
                WarnOnce("legacy-font", "Could not switch legacy font: " + ex.Message);
            }
        }

        public static void EnsureTextMesh(TextMesh text, string translated)
        {
            if (!_enabled || text == null) return;
            Font font;
            try { font = text.font; }
            catch { font = null; }

            if (font != null &&
                !TextUtil.NeedsGlyphs(translated, delegate (char c) { return LegacyHasGlyph(font, c); }))
                return;

            Font fallback = GetOrCreateLegacyFallback();
            if (fallback == null || fallback == font) return;
            try
            {
                text.font = fallback;
                TranslationPlugin.LogInfo("Switched TextMesh to fallback font: " + fallback.name);
            }
            catch (Exception ex)
            {
                WarnOnce("textmesh-font", "Could not switch TextMesh font: " + ex.Message);
            }
        }

        private static bool LegacyHasGlyph(Font font, char c)
        {
            if (font == null) return false;
            try { return font.HasCharacter(c); }
            catch { return true; }
        }

        private static Font GetOrCreateLegacyFallback()
        {
            if (_legacyFallback != null) return _legacyFallback;
            if (_legacyFallbackTried) return null;
            _legacyFallbackTried = true;

            for (int i = 0; i < _fontNames.Length; i++)
            {
                try
                {
                    Font font = Font.CreateDynamicFontFromOSFont(_fontNames[i], 24);
                    if (font != null)
                    {
                        font.name = "UnityTranslator Legacy (" + _targetLanguage + ")";
                        _legacyFallback = font;
                        return font;
                    }
                }
                catch { }
            }

            try
            {
                Font font = Font.CreateDynamicFontFromOSFont(_fontNames, 24);
                if (font != null)
                {
                    _legacyFallback = font;
                    return font;
                }
            }
            catch { }

            TranslationPlugin.LogWarning("Could not create a legacy UI fallback font for " + _targetLanguage);
            return null;
        }

        internal static string GetSampleText(string targetLanguage)
        {
            string target = (targetLanguage ?? string.Empty).ToLowerInvariant();
            if (target.StartsWith("zh", StringComparison.Ordinal)) return "你好";
            if (target.StartsWith("ja", StringComparison.Ordinal)) return "こんにちは";
            if (target.StartsWith("ko", StringComparison.Ordinal)) return "안녕하세요";
            if (target.StartsWith("ru", StringComparison.Ordinal)) return "Привет";
            if (target.StartsWith("uk", StringComparison.Ordinal)) return "Привіт";
            if (target.StartsWith("be", StringComparison.Ordinal)) return "Прывітанне";
            if (target.StartsWith("bg", StringComparison.Ordinal)) return "Здравей";
            if (target.StartsWith("sr", StringComparison.Ordinal)) return "Здраво";
            if (target.StartsWith("mk", StringComparison.Ordinal)) return "Здраво";
            if (target.StartsWith("kk", StringComparison.Ordinal)) return "Сәлем";
            if (target.StartsWith("el", StringComparison.Ordinal)) return "Γειά";
            if (target.StartsWith("he", StringComparison.Ordinal)) return "שלום";
            if (target.StartsWith("ar", StringComparison.Ordinal)) return "مرحبا";
            if (target.StartsWith("fa", StringComparison.Ordinal)) return "سلام";
            if (target.StartsWith("ur", StringComparison.Ordinal)) return "ہیلو";
            if (target.StartsWith("hi", StringComparison.Ordinal) ||
                target.StartsWith("mr", StringComparison.Ordinal) ||
                target.StartsWith("ne", StringComparison.Ordinal)) return "नमस्ते";
            if (target.StartsWith("th", StringComparison.Ordinal)) return "สวัสดี";
            if (target.StartsWith("ka", StringComparison.Ordinal)) return "გამარჯობა";
            if (target.StartsWith("hy", StringComparison.Ordinal)) return "Բարեւ";
            return "Hello";
        }

        internal static void WarnOnce(string key, string message)
        {
            lock (_warnings)
            {
                if (!_warnings.Add(key)) return;
            }
            TranslationPlugin.LogWarning(message);
        }
    }
}
