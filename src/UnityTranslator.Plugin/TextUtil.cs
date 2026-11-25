using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace UnityTranslator
{
    internal static class LanguageUtil
    {
        [DllImport("kernel32.dll")]
        private static extern ushort GetUserDefaultUILanguage();

        public static string ResolveTargetLanguage(string configured)
        {
            if (!string.IsNullOrEmpty(configured) &&
                !configured.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return Normalize(configured);
            }

            string system = GetSystemLanguage();
            return Normalize(system);
        }

        public static string GetSystemLanguage()
        {
            try
            {
                ushort lcid = GetUserDefaultUILanguage();
                if (lcid != 0)
                {
                    CultureInfo culture = CultureInfo.GetCultureInfo((int)lcid);
                    if (culture != null && !string.IsNullOrEmpty(culture.Name))
                        return culture.Name;
                }
            }
            catch
            {
                // Non-Windows platform or a missing P/Invoke target.
            }

            try
            {
                CultureInfo installed = CultureInfo.InstalledUICulture;
                if (installed != null && !string.IsNullOrEmpty(installed.Name))
                    return installed.Name;
            }
            catch { }

            try
            {
                CultureInfo current = CultureInfo.CurrentUICulture;
                if (current != null && !string.IsNullOrEmpty(current.Name))
                    return current.Name;
            }
            catch { }

            return "en";
        }

        /// <summary>Converts a Windows culture name to a Google Translate language code.</summary>
        public static string Normalize(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "en";
            code = code.Trim().Replace('_', '-');
            string lower = code.ToLowerInvariant();

            switch (lower)
            {
                case "zh": case "zh-hans": case "zh-cn": case "zh-sg": return "zh-CN";
                case "zh-hant": case "zh-tw": case "zh-hk": case "zh-mo": return "zh-TW";
                case "pt-br": case "pt": return "pt";
                case "pt-pt": return "pt-PT";
                case "nb": case "nb-no": case "nn": case "nn-no": return "no";
                case "iw": case "iw-il": return "he";
                case "in": case "in-id": return "id";
                case "ji": return "yi";
                case "jw": case "jw-id": return "jv";
                case "mo": return "ro";
                case "fil": case "fil-ph": return "tl";
                case "sr-latn": case "sr-latn-rs": return "sr";
            }

            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(code);
                string name = culture.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    string n = name.ToLowerInvariant();
                    if (n.StartsWith("zh", StringComparison.Ordinal))
                        return (n.Contains("tw") || n.Contains("hk") || n.Contains("mo")) ? "zh-TW" : "zh-CN";
                    if (n.StartsWith("pt-pt", StringComparison.Ordinal)) return "pt-PT";
                    if (n.StartsWith("pt", StringComparison.Ordinal)) return "pt";
                    if (n.StartsWith("sr", StringComparison.Ordinal)) return "sr";
                    if (n.StartsWith("nb", StringComparison.Ordinal) || n.StartsWith("nn", StringComparison.Ordinal)) return "no";
                    string two = culture.TwoLetterISOLanguageName;
                    if (!string.IsNullOrEmpty(two) && two.Length == 2 && two != "iv")
                        return two;
                }
            }
            catch { }

            int dash = lower.IndexOf('-');
            string baseCode = dash > 0 ? lower.Substring(0, dash) : lower;
            return baseCode.Length == 0 ? "en" : baseCode;
        }

        public static string FriendlyName(string code)
        {
            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(code);
                if (!string.IsNullOrEmpty(culture.DisplayName)) return culture.DisplayName;
            }
            catch { }
            return code;
        }

        /// <summary>
        /// Fast, conservative check that text already appears to be in the target language.
        /// It only returns true for scripts where a match is a strong signal, so mixed
        /// text (brand names, player names, numbers) still goes through translation.
        /// </summary>
        public static bool IsProbablyTarget(string text, string targetLanguage)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(targetLanguage)) return false;
            string visible = TextUtil.StripForAnalysis(text);
            if (!TextUtil.HasLetters(visible)) return false;

            string target = targetLanguage.ToLowerInvariant();
            int dash = target.IndexOf('-');
            string baseCode = dash > 0 ? target.Substring(0, dash) : target;

            switch (baseCode)
            {
                case "ru":
                    return MostlyScript(visible, IsCyrillic) && !ContainsAny(visible, "іїєґў");
                case "uk":
                    return MostlyScript(visible, IsCyrillic) && ContainsAny(visible, "іїєґ");
                case "be":
                    return MostlyScript(visible, IsCyrillic) && ContainsAny(visible, "ў");
                case "bg":
                    return MostlyScript(visible, IsCyrillic) && ContainsAny(visible, "ъ") &&
                           !ContainsAny(visible, "ыэё");
                case "sr":
                    return MostlyScript(visible, IsCyrillic) && ContainsAny(visible, "ђћџљњ");
                case "mk":
                    return MostlyScript(visible, IsCyrillic) && ContainsAny(visible, "ѓќѕјљњџ");
                case "kk":
                    return MostlyScript(visible, IsCyrillic) && ContainsAny(visible, "әғқңөұүһі");
                case "el":
                    return MostlyScript(visible, IsGreek);
                case "he":
                    return MostlyScript(visible, IsHebrew);
                case "ar":
                    return MostlyScript(visible, IsArabic);
                case "fa":
                    return MostlyScript(visible, IsArabic) && ContainsAny(visible, "پچژگ");
                case "ur":
                    return MostlyScript(visible, IsArabic) && ContainsAny(visible, "ٹڈڑںے");
                case "hi":
                case "mr":
                case "ne":
                    return MostlyScript(visible, IsDevanagari);
                case "th":
                    return MostlyScript(visible, IsThai);
                case "ka":
                    return MostlyScript(visible, IsGeorgian);
                case "hy":
                    return MostlyScript(visible, IsArmenian);
                case "ko":
                    return MostlyScript(visible, IsHangul);
                case "ja":
                    return MostlyScript(visible, IsKanaOrHan) && ContainsAny(visible, "\u3040\u30A0");
                case "zh":
                    return MostlyScript(visible, IsHan);
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when most letters already use the target script. This tolerates Latin
        /// usernames, brand names and numbers mixed into text that is already translated,
        /// so such strings do not cause pointless "translation" requests.
        /// </summary>
        private static bool MostlyScript(string text, Func<char, bool> isScript)
        {
            const double Threshold = 0.6;
            int total = 0;
            int matching = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!char.IsLetter(c)) continue;
                total++;
                if (isScript(c)) matching++;
            }
            return total > 0 && matching >= total * Threshold;
        }

        private static bool ContainsAny(string text, string chars)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (chars.IndexOf(text[i]) >= 0) return true;
            }
            return false;
        }

        public static bool IsCyrillic(char c)
        {
            return (c >= '\u0400' && c <= '\u052F') ||
                   (c >= '\u2DE0' && c <= '\u2DFF') ||
                   (c >= '\uA640' && c <= '\uA69F') ||
                   (c >= '\u1C80' && c <= '\u1C8F');
        }

        public static bool IsGreek(char c)
        {
            return (c >= '\u0370' && c <= '\u03FF') ||
                   (c >= '\u1F00' && c <= '\u1FFF');
        }

        public static bool IsHebrew(char c)
        {
            return c >= '\u0590' && c <= '\u05FF';
        }

        public static bool IsArabic(char c)
        {
            return (c >= '\u0600' && c <= '\u06FF') ||
                   (c >= '\u0750' && c <= '\u077F') ||
                   (c >= '\u08A0' && c <= '\u08FF') ||
                   (c >= '\uFB50' && c <= '\uFDFF') ||
                   (c >= '\uFE70' && c <= '\uFEFF');
        }

        public static bool IsDevanagari(char c)
        {
            return c >= '\u0900' && c <= '\u097F';
        }

        public static bool IsThai(char c)
        {
            return c >= '\u0E00' && c <= '\u0E7F';
        }

        public static bool IsGeorgian(char c)
        {
            return (c >= '\u10A0' && c <= '\u10FF') ||
                   (c >= '\u2D00' && c <= '\u2D2F');
        }

        public static bool IsArmenian(char c)
        {
            return c >= '\u0530' && c <= '\u058F';
        }

        public static bool IsHangul(char c)
        {
            return (c >= '\uAC00' && c <= '\uD7AF') ||
                   (c >= '\u1100' && c <= '\u11FF') ||
                   (c >= '\u3130' && c <= '\u318F');
        }

        public static bool IsHan(char c)
        {
            return (c >= '\u3400' && c <= '\u4DBF') ||
                   (c >= '\u4E00' && c <= '\u9FFF') ||
                   (c >= '\uF900' && c <= '\uFAFF');
        }

        public static bool IsKanaOrHan(char c)
        {
            return (c >= '\u3040' && c <= '\u309F') ||
                   (c >= '\u30A0' && c <= '\u30FF') ||
                   IsHan(c);
        }
    }

    internal static class TextUtil
    {
        public static int MaxTextLength = 10000;
        public static int MinLetters = 2;

        public static bool HasLetters(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetter(text[i])) return true;
            }
            return false;
        }

        public static int CountLetters(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetter(text[i])) count++;
            }
            return count;
        }

        /// <summary>Retains visible characters and removes markup, placeholders and escapes.</summary>
        public static string StripForAnalysis(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        i = close;
                        continue;
                    }
                }

                if (c == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > i && close - i <= 64)
                    {
                        i = close;
                        continue;
                    }
                }

                if (c == '%' && i + 1 < text.Length)
                {
                    // printf-style placeholders: %s, %d, %1$s, %02d
                    int j = i + 1;
                    while (j < text.Length && (char.IsDigit(text[j]) || text[j] == '$')) j++;
                    if (j < text.Length && "sdifuxXc".IndexOf(text[j]) >= 0)
                    {
                        i = j;
                        continue;
                    }
                }

                if (c == '\\' && i + 1 < text.Length)
                {
                    char next = text[i + 1];
                    if (next == 'n' || next == 'r' || next == 't')
                    {
                        i++;
                        continue;
                    }
                }

                if (c == '\n' || c == '\r' || c == '\t') continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static bool IsEligible(string text, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length > MaxTextLength) return false;
            if (text.IndexOf('\0') >= 0) return false;

            string visible = StripForAnalysis(text);
            if (!HasLetters(visible)) return false;
            if (CountLetters(visible) < MinLetters) return false;
            if (LanguageUtil.IsProbablyTarget(visible, targetLanguage)) return false;
            if (IsProbablyTechnical(text)) return false;
            return true;
        }

        /// <summary>True when translated text needs characters that the original font lacks.</summary>
        public static bool NeedsGlyphs(string translated, Predicate<char> hasGlyph)
        {
            if (string.IsNullOrEmpty(translated) || hasGlyph == null) return false;
            string visible = StripForAnalysis(translated);
            for (int i = 0; i < visible.Length; i++)
            {
                char c = visible[i];
                if (!char.IsLetterOrDigit(c)) continue;
                if (!hasGlyph(c)) return true;
            }
            return false;
        }

        private static bool IsProbablyTechnical(string text)
        {
            string t = text.Trim();
            if (t.Length == 0) return true;

            string lower = t.ToLowerInvariant();
            if (lower.StartsWith("http://", StringComparison.Ordinal) ||
                lower.StartsWith("https://", StringComparison.Ordinal) ||
                lower.StartsWith("www.", StringComparison.Ordinal) ||
                lower.StartsWith("file://", StringComparison.Ordinal) ||
                lower.IndexOf("://", StringComparison.Ordinal) >= 0)
                return true;

            if (t.Length >= 3 && char.IsLetter(t[0]) && t[1] == ':' &&
                (t[2] == '\\' || t[2] == '/'))
                return true;

            bool hasWhitespace = false;
            bool hasDigit = false;
            bool hasLetter = false;
            bool hasSeparator = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (char.IsWhiteSpace(c)) hasWhitespace = true;
                if (char.IsDigit(c)) hasDigit = true;
                if (char.IsLetter(c)) hasLetter = true;
                if (c == '_' || c == '/' || c == '\\' || c == '@') hasSeparator = true;
            }

            if (!hasWhitespace && hasSeparator && hasLetter) return true;
            if (!hasWhitespace && hasDigit && hasLetter && t.Length >= 24) return true;

            // Hex colour, GUID, version, file name.
            if (t.Length >= 6 && t[0] == '#' && IsHex(t, 1)) return true;
            if (IsGuidLike(t)) return true;
            if (IsVersionLike(t)) return true;
            if (!hasWhitespace && t.IndexOf('.') > 0 && t.Length >= 3 &&
                (lower.EndsWith(".png", StringComparison.Ordinal) ||
                 lower.EndsWith(".jpg", StringComparison.Ordinal) ||
                 lower.EndsWith(".json", StringComparison.Ordinal) ||
                 lower.EndsWith(".txt", StringComparison.Ordinal) ||
                 lower.EndsWith(".dll", StringComparison.Ordinal) ||
                 lower.EndsWith(".exe", StringComparison.Ordinal) ||
                 lower.EndsWith(".asset", StringComparison.Ordinal) ||
                 lower.EndsWith(".bundle", StringComparison.Ordinal)))
                return true;

            return false;
        }

        private static bool IsHex(string s, int start)
        {
            if (s.Length - start < 3) return false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static bool IsGuidLike(string s)
        {
            if (s.Length != 32 && s.Length != 36) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '-' && s.Length == 36) continue;
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static bool IsVersionLike(string s)
        {
            if (s.Length < 3) return false;
            char c0 = s[0];
            if (!(c0 == 'v' || c0 == 'V' || char.IsDigit(c0))) return false;
            bool digit = false;
            bool dot = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsDigit(c)) digit = true;
                else if (c == '.') dot = true;
                else if (c != 'v' && c != 'V' && c != '-' && c != '_' && c != '+' && !char.IsLetter(c))
                    return false;
            }
            return digit && dot;
        }
    }
}
