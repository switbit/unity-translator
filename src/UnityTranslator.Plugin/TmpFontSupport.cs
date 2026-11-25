using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace UnityTranslator
{
    /// <summary>
    /// TextMeshPro font fallback. Kept in a separate type so the plugin can still
    /// load and translate legacy Text/TextMesh in games without the TMP package.
    /// </summary>
    internal static class TmpFontSupport
    {
        // Stored as object so this type can be loaded even when Unity.TextMeshPro.dll
        // is absent. Every access below is inside TMP-only code paths.
        private static object _tmpFallback;
        private static bool _tmpFallbackTried;
        private static bool _globalFallbackAdded;

        public static void EnsureTmp(TMP_Text text, string translated)
        {
            if (!FontSupport.Enabled || text == null) return;
            TMP_FontAsset font;
            try { font = text.font; }
            catch { font = null; }

            if (font == null) return;
            if (!TextUtil.NeedsGlyphs(translated, delegate (char c) { return TmpHasGlyph(font, c); }))
                return;

            TMP_FontAsset fallback = GetOrCreateTmpFallback(font);
            if (fallback == null || fallback == font) return;

            AddFallbackToFont(font, fallback);
            AddGlobalFallback(fallback);
        }

        private static bool TmpHasGlyph(TMP_FontAsset font, char c)
        {
            if (font == null) return true;
            try
            {
                // Ask TMP to add the glyph from the font file if it is dynamic.
                return font.HasCharacter(c, false, true);
            }
            catch
            {
                try { return font.HasCharacter((int)c); }
                catch { return true; }
            }
        }

        private static TMP_FontAsset GetOrCreateTmpFallback(TMP_FontAsset currentFont)
        {
            if (_tmpFallback != null) return (TMP_FontAsset)_tmpFallback;
            if (_tmpFallbackTried) return null;
            _tmpFallbackTried = true;

            string sample = FontSupport.GetSampleText(FontSupport.TargetLanguage);
            try
            {
                TMP_FontAsset[] loaded = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (loaded != null)
                {
                    for (int i = 0; i < loaded.Length && i < 256; i++)
                    {
                        TMP_FontAsset candidate = loaded[i];
                        if (candidate == null || candidate == currentFont) continue;
                        if (SupportsSample(candidate, sample))
                        {
                            _tmpFallback = candidate;
                            TranslationPlugin.LogInfo("Using existing TMP font as fallback: " + candidate.name);
                            return candidate;
                        }
                    }
                }
            }
            catch { }

            string[] names = FontSupport.FontNames;
            for (int i = 0; i < names.Length; i++)
            {
                TMP_FontAsset created = TryCreateTmpFont(names[i]);
                if (created != null && SupportsSample(created, sample))
                {
                    created.name = "UnityTranslator Fallback (" + FontSupport.TargetLanguage + ")";
                    try { created.hideFlags = HideFlags.DontSave; } catch { }
                    _tmpFallback = created;
                    TranslationPlugin.LogInfo("Created TMP fallback font from OS font: " + names[i]);
                    return created;
                }
            }

            TranslationPlugin.LogWarning("Could not create a TMP fallback font for " + FontSupport.TargetLanguage +
                                         ". Some characters may be missing.");
            return null;
        }

        private static TMP_FontAsset TryCreateTmpFont(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;

            // TMP 2.x/3.x: create directly from an installed OS font family.
            try
            {
                MethodInfo method = typeof(TMP_FontAsset).GetMethod(
                    "CreateFontAsset",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(string), typeof(int) },
                    null);
                if (method != null)
                {
                    TMP_FontAsset asset = method.Invoke(null, new object[] { familyName, "Regular", 90 }) as TMP_FontAsset;
                    if (Prepare(asset)) return asset;
                }
            }
            catch { }

            // Fallback: create a dynamic UnityEngine.Font and convert it.
            try
            {
                Font font = Font.CreateDynamicFontFromOSFont(familyName, 90);
                if (font == null) return null;

                MethodInfo oneArg = typeof(TMP_FontAsset).GetMethod(
                    "CreateFontAsset",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Font) },
                    null);
                if (oneArg != null)
                {
                    TMP_FontAsset asset = oneArg.Invoke(null, new object[] { font }) as TMP_FontAsset;
                    if (Prepare(asset)) return asset;
                }

                MethodInfo manyArgs = typeof(TMP_FontAsset).GetMethod(
                    "CreateFontAsset",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(Font), typeof(int), typeof(int), typeof(UnityEngine.TextCore.LowLevel.GlyphRenderMode),
                        typeof(int), typeof(int), typeof(AtlasPopulationMode), typeof(bool)
                    },
                    null);
                if (manyArgs != null)
                {
                    object renderMode = Enum.Parse(typeof(UnityEngine.TextCore.LowLevel.GlyphRenderMode), "SDFAA");
                    TMP_FontAsset asset = manyArgs.Invoke(null, new object[]
                    {
                        font, 90, 9, renderMode, 1024, 1024, AtlasPopulationMode.Dynamic, true
                    }) as TMP_FontAsset;
                    if (Prepare(asset)) return asset;
                }
            }
            catch { }

            return null;
        }

        private static bool Prepare(TMP_FontAsset asset)
        {
            if (asset == null) return false;
            try { asset.atlasPopulationMode = AtlasPopulationMode.Dynamic; } catch { }
            try { asset.isMultiAtlasTexturesEnabled = true; } catch { }
            return true;
        }

        private static bool SupportsSample(TMP_FontAsset font, string sample)
        {
            if (font == null || string.IsNullOrEmpty(sample)) return false;
            for (int i = 0; i < sample.Length; i++)
            {
                char c = sample[i];
                if (!char.IsLetter(c)) continue;
                if (!TmpHasGlyph(font, c)) return false;
            }
            return true;
        }

        private static void AddFallbackToFont(TMP_FontAsset font, TMP_FontAsset fallback)
        {
            if (font == null || fallback == null || font == fallback) return;
            try
            {
                List<TMP_FontAsset> table = font.fallbackFontAssetTable;
                if (table == null)
                {
                    table = new List<TMP_FontAsset>();
                    font.fallbackFontAssetTable = table;
                }
                if (!table.Contains(fallback)) table.Add(fallback);
            }
            catch (Exception ex)
            {
                FontSupport.WarnOnce("tmp-table", "Could not add TMP fallback table: " + ex.Message);
            }
        }

        private static void AddGlobalFallback(TMP_FontAsset fallback)
        {
            if (_globalFallbackAdded || fallback == null) return;
            _globalFallbackAdded = true;
            try
            {
                List<TMP_FontAsset> list = TMP_Settings.fallbackFontAssets;
                if (list == null) list = new List<TMP_FontAsset>();
                if (!list.Contains(fallback)) list.Add(fallback);
                TMP_Settings.fallbackFontAssets = list;
            }
            catch (Exception ex)
            {
                FontSupport.WarnOnce("tmp-global", "Could not register global TMP fallback: " + ex.Message);
            }
        }
    }
}
