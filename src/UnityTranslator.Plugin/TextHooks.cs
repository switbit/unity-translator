using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnityTranslator
{
    internal static class TextHooks
    {
        [ThreadStatic]
        internal static bool Applying;

        internal static float StableDelaySeconds = 0.4f;
        internal static float RevealGraceSeconds = 1.5f;
        internal static string[] ExcludedTokens = new string[0];

        private sealed class TextState
        {
            public WeakReference<Component> Reference;
            public TextKind Kind;
            public string LastText;
            public string SourceText;
            public string AppliedText;
            public float LastChange;
            public float NextAttempt;
            public float NextVisualCheck;
            public int LastMaxVisible = -1;
            public bool HasVisualSignature;
            public long VisualSignature;
            public bool SawReveal;
            public float RevealFirstSeenAt;
            public float RevealCompleteAt;
            public int RevealTrailingChars;
        }

        private static readonly Dictionary<int, TextState> _states = new Dictionary<int, TextState>();
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _excludedLogged = new HashSet<string>(StringComparer.Ordinal);
        private static bool _tmpScanUnavailable;
        private static float _lastProcessTime = -1f;

        internal static void LegacySet(Text __instance, ref string value)
        {
            SafeHandle(__instance, value, TextKind.UguiText);
        }

        internal static void TmpSet(TMP_Text __instance, ref string value)
        {
            SafeHandle(__instance, value, TextKind.TmpText);
        }

        internal static void TmpSetText(TMP_Text __instance, ref string sourceText)
        {
            SafeHandle(__instance, sourceText, TextKind.TmpText);
        }

        internal static void TextMeshSet(TextMesh __instance, ref string value)
        {
            SafeHandle(__instance, value, TextKind.TextMeshText);
        }

        internal static void TmpSetTextBuilder(TMP_Text __instance, StringBuilder sourceText)
        {
            if (sourceText == null) return;
            try
            {
                // Only observe. Never rewrite the builder: some games reuse it for
                // their own state and logic.
                SafeHandle(__instance, sourceText.ToString(), TextKind.TmpText);
            }
            catch (Exception ex)
            {
                WarnOnce("builder-hook", ex);
            }
        }

        private static void SafeHandle(Component component, string value, TextKind kind)
        {
            try
            {
                Handle(component, value, kind);
            }
            catch (Exception ex)
            {
                WarnOnce("handle", ex);
            }
        }

        private static void Handle(Component component, string value, TextKind kind)
        {
            if (Applying || component == null) return;
            if (string.IsNullOrEmpty(value)) return;
            TranslationService service = TranslationPlugin.Service;
            if (service == null || !TranslationPlugin.Ready) return;
            if (service.IsTranslatedOutput(value)) return;
            if (IsInputText(component, kind)) return;
            if (IsExcluded(component)) return;
            if (!TextUtil.IsEligible(value, TranslationPlugin.TargetLanguage)) return;

            // Do not translate here. Typewriter effects and marquees set text many
            // times per second; replacing those intermediate strings both looks wrong
            // and can break scripts that read the text back. Only remember the text.
            Record(component, kind, value);
        }

        private static void Record(Component component, TextKind kind, string value)
        {
            int id = component.GetInstanceID();
            float now = Time.unscaledTime;
            TextState state;
            if (!_states.TryGetValue(id, out state))
            {
                state = new TextState();
                state.Reference = new WeakReference<Component>(component);
                state.Kind = kind;
                state.LastText = value;
                state.SourceText = value;
                state.LastChange = now;
                _states[id] = state;
                return;
            }

            state.Reference = new WeakReference<Component>(component);
            state.Kind = kind;
            if (!string.Equals(state.LastText, value, StringComparison.Ordinal))
            {
                state.LastText = value;
                state.SourceText = value;
                state.AppliedText = null;
                state.LastChange = now;
                state.NextAttempt = 0f;
                state.NextVisualCheck = 0f;
                state.LastMaxVisible = -1;
                state.HasVisualSignature = false;
                state.SawReveal = false;
                state.RevealFirstSeenAt = 0f;
                state.RevealCompleteAt = 0f;
                state.RevealTrailingChars = 0;
            }
        }

        private static bool IsStable(TextState state, float now)
        {
            if (now - state.LastChange < StableDelaySeconds) return false;

            if (state.Kind == TextKind.TmpText && state.SawReveal)
            {
                if (state.RevealCompleteAt <= 0f)
                {
                    // A partial reveal that never changes is usually static styling
                    // (for example faded decoration text). Do not block it forever.
                    if (state.RevealFirstSeenAt > 0f && now - state.RevealFirstSeenAt < 15f)
                        return false;
                }
                else
                {
                    // TypeLine keeps running for a few non-visible trailing characters
                    // (spaces/quotes) after the last visible glyph is fully revealed.
                    // Replacing text during that window invalidates its cached colour
                    // arrays and makes ApplyReveal throw, freezing the cutscene.
                    float trailing = Math.Max(0, state.RevealTrailingChars);
                    float grace = RevealGraceSeconds + Math.Min(3f, trailing / 30f);
                    if (now - state.RevealCompleteAt < grace) return false;
                }
            }

            return true;
        }

        private struct TmpVisualState
        {
            public int CharacterCount;
            public int LastVisibleIndex;
            public int MaxVisible;
            public long Signature;
            public bool Mixed;
            public bool Complete;
        }

        /// <summary>
        /// Some games set the complete string once and then reveal it by changing TMP
        /// vertex alpha. Replacing the text while that coroutine is still alive
        /// invalidates its cached TMP_TextInfo/colour arrays and can make the coroutine
        /// throw, freezing the scene. Track the alpha animation and wait until it has
        /// been visually still for a safety period before translating.
        /// </summary>
        private static void UpdateTmpReveal(Component component, TextState state, float now)
        {
            if (state.Kind != TextKind.TmpText) return;
            if (now < state.NextVisualCheck) return;
            state.NextVisualCheck = now + 0.1f;

            TmpVisualState visual;
            bool sampled;
            try
            {
                sampled = TrySampleTmp(component, out visual);
            }
            catch
            {
                return;
            }
            if (!sampled) return;

            bool firstSample = !state.HasVisualSignature;
            bool changed = !firstSample &&
                           (visual.Signature != state.VisualSignature ||
                            visual.MaxVisible != state.LastMaxVisible);

            state.HasVisualSignature = true;
            state.VisualSignature = visual.Signature;
            state.LastMaxVisible = visual.MaxVisible;

            if (changed)
            {
                state.LastChange = now;
                state.SawReveal = true;
            }
            if (visual.Mixed) state.SawReveal = true;

            if (!visual.Complete)
            {
                state.SawReveal = true;
                state.RevealCompleteAt = 0f;
                if (state.RevealFirstSeenAt <= 0f) state.RevealFirstSeenAt = now;
            }
            else
            {
                state.RevealFirstSeenAt = 0f;
                if (state.SawReveal && state.RevealCompleteAt <= 0f)
                {
                    state.RevealCompleteAt = now;
                    state.RevealTrailingChars = Math.Max(0, visual.CharacterCount - visual.LastVisibleIndex - 1);
                }
            }

            // No animation was ever observed: the text was displayed fully at once.
            if (firstSample && visual.Complete && !visual.Mixed)
            {
                state.SawReveal = false;
                state.RevealCompleteAt = 0f;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TrySampleTmp(Component component, out TmpVisualState visual)
        {
            visual = new TmpVisualState { MaxVisible = -1, LastVisibleIndex = -1 };

            TMP_Text text = component as TMP_Text;
            if (text == null) return false;

            TMP_TextInfo info = text.textInfo;
            if (info == null) return false;
            int count = info.characterCount;
            visual.MaxVisible = text.maxVisibleCharacters;
            visual.CharacterCount = count;
            if (count <= 0 || count > 50000) return false;

            TMP_CharacterInfo[] characters = info.characterInfo;
            TMP_MeshInfo[] meshes = info.meshInfo;
            if (characters == null || meshes == null) return false;

            int limit = Math.Min(count, characters.Length);
            int visible = 0;
            int lowAlpha = 0;
            long signature = 17;

            for (int i = 0; i < limit; i++)
            {
                TMP_CharacterInfo character = characters[i];
                if (!character.isVisible) continue;
                int material = character.materialReferenceIndex;
                if (material < 0 || material >= meshes.Length) continue;
                Color32[] colors = meshes[material].colors32;
                if (colors == null) continue;
                int vertex = character.vertexIndex;
                if (vertex < 0 || vertex >= colors.Length) continue;

                byte alpha = colors[vertex].a;
                visible++;
                visual.LastVisibleIndex = i;
                if (alpha <= 16) lowAlpha++;

                signature = signature * 31 + alpha + i * 7;
            }

            bool showingAll = visual.MaxVisible < 0 || visual.MaxVisible >= count;
            visual.Signature = signature;
            visual.Mixed = lowAlpha > 0 && visible > lowAlpha;
            visual.Complete = visible == 0 || (showingAll && lowAlpha == 0);
            return true;
        }

        /// <summary>Called every frame from the plugin Update loop.</summary>
        internal static void ProcessStableText()
        {
            if (Applying || _states.Count == 0) return;
            TranslationService service = TranslationPlugin.Service;
            if (service == null || !TranslationPlugin.Ready) return;

            float now = Time.unscaledTime;

            // If the game hitched for several hundred milliseconds, its reveal
            // coroutine may not have resumed yet. Unity can call our Update before
            // that coroutine, so give it another grace period instead of replacing
            // the text while its cached mesh data is still live.
            bool longFrame = _lastProcessTime >= 0f && now - _lastProcessTime > 0.25f;
            _lastProcessTime = now;
            if (longFrame)
            {
                foreach (KeyValuePair<int, TextState> pair in _states)
                {
                    TextState hitched = pair.Value;
                    if (hitched.Kind != TextKind.TmpText || !hitched.SawReveal) continue;
                    hitched.LastChange = now;
                    hitched.RevealCompleteAt = 0f;
                    hitched.NextVisualCheck = 0f;
                }
            }

            List<int> dead = null;

            foreach (KeyValuePair<int, TextState> pair in _states)
            {
                TextState state = pair.Value;
                Component component = null;
                bool alive = state.Reference != null && state.Reference.TryGetTarget(out component);
                if (!alive || component == null)
                {
                    if (dead == null) dead = new List<int>();
                    dead.Add(pair.Key);
                    continue;
                }

                UpdateTmpReveal(component, state, now);
                if (now < state.NextAttempt) continue;
                if (!IsStable(state, now)) continue;

                state.NextAttempt = now + 1f;
                try
                {
                    RequestExisting(component, state.Kind);
                }
                catch (Exception ex)
                {
                    WarnOnce("stable-request", ex);
                }
            }

            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++) _states.Remove(dead[i]);
            }
        }

        internal static void LegacyOnEnable(Text __instance)
        {
            SafeRequestExisting(__instance, TextKind.UguiText);
        }

        internal static void TmpOnEnableUGUI(TextMeshProUGUI __instance)
        {
            SafeRequestExisting(__instance, TextKind.TmpText);
        }

        internal static void TmpOnEnable3D(TextMeshPro __instance)
        {
            SafeRequestExisting(__instance, TextKind.TmpText);
        }

        private static void SafeRequestExisting(Component component, TextKind kind)
        {
            try
            {
                RequestExisting(component, kind);
            }
            catch (Exception ex)
            {
                WarnOnce("request-existing", ex);
            }
        }

        internal static void RequestExisting(Component component, TextKind kind)
        {
            if (Applying || component == null) return;
            TranslationService service = TranslationPlugin.Service;
            if (service == null || !TranslationPlugin.Ready) return;
            if (IsInputText(component, kind)) return;
            if (IsExcluded(component)) return;

            if (kind == TextKind.TmpText)
            {
                RequestExistingTmp(component);
                return;
            }

            string current;
            try
            {
                if (kind == TextKind.UguiText)
                {
                    Text ui = component as Text;
                    if (ui == null) return;
                    current = ui.text;
                }
                else
                {
                    TextMesh mesh = component as TextMesh;
                    if (mesh == null) return;
                    current = mesh.text;
                }
            }
            catch
            {
                return;
            }

            RequestCurrent(component, kind, current);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RequestExistingTmp(Component component)
        {
            TMP_Text tmp = component as TMP_Text;
            if (tmp == null) return;
            string current;
            try { current = tmp.text; }
            catch { return; }
            RequestCurrent(component, TextKind.TmpText, current);
        }

        private static void RequestCurrent(Component component, TextKind kind, string current)
        {
            if (string.IsNullOrEmpty(current)) return;
            TranslationService service = TranslationPlugin.Service;
            if (service == null || !TranslationPlugin.Ready) return;
            if (service.IsTranslatedOutput(current)) return;
            if (!TextUtil.IsEligible(current, TranslationPlugin.TargetLanguage)) return;

            Record(component, kind, current);

            TextState state;
            if (!_states.TryGetValue(component.GetInstanceID(), out state)) return;
            if (!IsStable(state, Time.unscaledTime)) return;

            string translated;
            if (service.TryGetCached(current, out translated))
            {
                if (!string.IsNullOrEmpty(translated) &&
                    !string.Equals(translated, current, StringComparison.Ordinal))
                {
                    service.RememberOutput(translated);
                    ApplyNow(new TextTarget(component, kind, current), translated);
                }
                return;
            }

            service.Request(current, new TextTarget(component, kind, current));
        }

        internal static void ApplyNow(TextTarget target, string translated)
        {
            if (target == null || string.IsNullOrEmpty(translated)) return;
            if (string.Equals(target.Original, translated, StringComparison.Ordinal)) return;

            Component component = target.GetComponent();
            if (component == null) return;

            try
            {
                switch (target.Kind)
                {
                    case TextKind.TmpText:
                        ApplyTmp(target, component, translated);
                        break;
                    case TextKind.UguiText:
                    {
                        Text text = component as Text;
                        if (text == null) return;
                        if (!string.Equals(text.text, target.Original, StringComparison.Ordinal)) return;
                        FontSupport.EnsureLegacy(text, translated);
                        Applying = true;
                        try { text.text = translated; }
                        finally { Applying = false; }
                        RememberApplied(component, target.Original, translated);
                        break;
                    }
                    default:
                    {
                        TextMesh text = component as TextMesh;
                        if (text == null) return;
                        if (!string.Equals(text.text, target.Original, StringComparison.Ordinal)) return;
                        FontSupport.EnsureTextMesh(text, translated);
                        Applying = true;
                        try { text.text = translated; }
                        finally { Applying = false; }
                        RememberApplied(component, target.Original, translated);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Applying = false;
                WarnOnce("apply", ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyTmp(TextTarget target, Component component, string translated)
        {
            TMP_Text text = component as TMP_Text;
            if (text == null) return;
            if (!string.Equals(text.text, target.Original, StringComparison.Ordinal)) return;
            TmpFontSupport.EnsureTmp(text, translated);
            Applying = true;
            try { text.text = translated; }
            finally { Applying = false; }
            RememberApplied(component, target.Original, translated);
        }

        private static void RememberApplied(Component component, string source, string translated)
        {
            if (component == null) return;
            TextState state;
            if (!_states.TryGetValue(component.GetInstanceID(), out state)) return;
            state.SourceText = source;
            state.AppliedText = translated;
        }

        /// <summary>
        /// Restores the original text of every component that currently shows our
        /// translation, then lets the normal pipeline translate it again. Useful when
        /// a game renders a translated string incorrectly or leaves stale glyphs.
        /// </summary>
        internal static void ReloadText()
        {
            if (Applying || _states.Count == 0) return;
            TranslationService service = TranslationPlugin.Service;
            if (service == null || !TranslationPlugin.Ready) return;

            float now = Time.unscaledTime;
            int reloaded = 0;
            List<TextState> snapshot = new List<TextState>(_states.Values);

            for (int i = 0; i < snapshot.Count; i++)
            {
                TextState state = snapshot[i];
                Component component;
                if (state.Reference == null || !state.Reference.TryGetTarget(out component) || component == null)
                    continue;
                if (string.IsNullOrEmpty(state.SourceText) || string.IsNullOrEmpty(state.AppliedText))
                    continue;

                // Never touch text that is still being revealed or is inside its
                // post-reveal safety window; replacing it could kill the game's
                // typewriter coroutine, exactly like the bug this guard protects.
                if (!IsStable(state, now)) continue;

                string current;
                if (!TryGetText(component, state.Kind, out current)) continue;
                if (!string.Equals(current, state.AppliedText, StringComparison.Ordinal))
                {
                    // The game already moved on to another page.
                    state.AppliedText = null;
                    continue;
                }

                try
                {
                    Applying = true;
                    try { SetText(component, state.Kind, state.SourceText); }
                    finally { Applying = false; }
                }
                catch (Exception ex)
                {
                    Applying = false;
                    WarnOnce("reload", ex);
                    continue;
                }

                state.LastText = state.SourceText;
                state.AppliedText = null;
                state.LastChange = now - Math.Max(StableDelaySeconds, 0.5f);
                state.NextAttempt = 0f;
                state.NextVisualCheck = 0f;
                state.LastMaxVisible = -1;
                state.HasVisualSignature = false;
                state.SawReveal = false;
                state.RevealFirstSeenAt = 0f;
                state.RevealCompleteAt = 0f;
                state.RevealTrailingChars = 0;
                reloaded++;
            }

            TranslationPlugin.LogInfo("Reloaded " + reloaded + " translated text(s).");
        }

        private static bool TryGetText(Component component, TextKind kind, out string value)
        {
            value = null;
            try
            {
                if (kind == TextKind.TmpText)
                {
                    TMP_Text tmp = component as TMP_Text;
                    if (tmp == null) return false;
                    value = tmp.text;
                    return true;
                }
                if (kind == TextKind.UguiText)
                {
                    Text text = component as Text;
                    if (text == null) return false;
                    value = text.text;
                    return true;
                }
                TextMesh mesh = component as TextMesh;
                if (mesh == null) return false;
                value = mesh.text;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetText(Component component, TextKind kind, string value)
        {
            if (kind == TextKind.TmpText)
            {
                TMP_Text tmp = component as TMP_Text;
                if (tmp != null) tmp.text = value;
            }
            else if (kind == TextKind.UguiText)
            {
                Text text = component as Text;
                if (text != null) text.text = value;
            }
            else
            {
                TextMesh mesh = component as TextMesh;
                if (mesh != null) mesh.text = value;
            }
        }

        internal static void ScanAll()
        {
            if (Applying) return;
            TranslationService service = TranslationPlugin.Service;
            if (service == null || !TranslationPlugin.Ready) return;

            if (!_tmpScanUnavailable)
            {
                try
                {
                    ScanTmpText();
                }
                catch (Exception ex)
                {
                    _tmpScanUnavailable = true;
                    TranslationPlugin.LogWarning("TextMeshPro scan unavailable: " + ex.Message);
                }
            }

            try
            {
                Text[] legacyTexts = UnityEngine.Object.FindObjectsOfType<Text>();
                int limit = Math.Min(legacyTexts.Length, 4000);
                for (int i = 0; i < limit; i++)
                {
                    Text text = legacyTexts[i];
                    if (text == null || !text.isActiveAndEnabled) continue;
                    SafeRequestExisting(text, TextKind.UguiText);
                }
            }
            catch (Exception ex)
            {
                TranslationPlugin.LogWarning("UI Text scan failed: " + ex.Message);
            }

            try
            {
                TextMesh[] meshes = UnityEngine.Object.FindObjectsOfType<TextMesh>();
                int limit = Math.Min(meshes.Length, 2000);
                for (int i = 0; i < limit; i++)
                {
                    TextMesh mesh = meshes[i];
                    if (mesh == null || !mesh.gameObject.activeInHierarchy) continue;
                    SafeRequestExisting(mesh, TextKind.TextMeshText);
                }
            }
            catch (Exception ex)
            {
                TranslationPlugin.LogWarning("TextMesh scan failed: " + ex.Message);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ScanTmpText()
        {
            TMP_Text[] tmpTexts = UnityEngine.Object.FindObjectsOfType<TMP_Text>();
            int limit = Math.Min(tmpTexts.Length, 4000);
            for (int i = 0; i < limit; i++)
            {
                TMP_Text text = tmpTexts[i];
                if (text == null || !text.isActiveAndEnabled) continue;
                SafeRequestExisting(text, TextKind.TmpText);
            }
        }

        private static void EnsureFont(Component component, TextKind kind, string translated)
        {
            try
            {
                if (kind == TextKind.TmpText) EnsureFontTmp(component, translated);
                else if (kind == TextKind.UguiText) FontSupport.EnsureLegacy(component as Text, translated);
                else FontSupport.EnsureTextMesh(component as TextMesh, translated);
            }
            catch { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnsureFontTmp(Component component, string translated)
        {
            TmpFontSupport.EnsureTmp(component as TMP_Text, translated);
        }

        private static bool IsInputText(Component component, TextKind kind)
        {
            if (component == null) return false;
            try
            {
                if (kind == TextKind.TmpText) return IsInputTmp(component);
                if (kind == TextKind.UguiText)
                {
                    Text text = component as Text;
                    if (text == null) return false;
                    InputField input = text.GetComponentInParent<InputField>();
                    return input != null && ReferenceEquals(input.textComponent, text);
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsInputTmp(Component component)
        {
            TMP_Text text = component as TMP_Text;
            if (text == null) return false;
            TMP_InputField input = text.GetComponentInParent<TMP_InputField>();
            return input != null && ReferenceEquals(input.textComponent, text);
        }

        internal static string GetObjectPath(Component component)
        {
            try
            {
                if (component == null || component.gameObject == null) return string.Empty;
                List<string> parts = new List<string>();
                Transform transform = component.transform;
                int depth = 0;
                while (transform != null && depth < 8)
                {
                    parts.Add(transform.name);
                    transform = transform.parent;
                    depth++;
                }
                parts.Reverse();
                string path = string.Join("/", parts.ToArray());
                string scene = component.gameObject.scene.name;
                if (!string.IsNullOrEmpty(scene)) return scene + ":" + path;
                return path;
            }
            catch
            {
                try { return component != null && component.gameObject != null ? component.gameObject.name : string.Empty; }
                catch { return string.Empty; }
            }
        }

        private static bool IsExcluded(Component component)
        {
            string[] tokens = ExcludedTokens;
            if (tokens == null || tokens.Length == 0 || component == null) return false;

            string path = GetObjectPath(component);
            string type = component.GetType().Name;
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrEmpty(token)) continue;
                if (path.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    type.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    LogExcluded(path);
                    return true;
                }
            }
            return false;
        }

        private static void LogExcluded(string path)
        {
            if (!TranslationPlugin.LogTranslations || string.IsNullOrEmpty(path)) return;
            lock (_excludedLogged)
            {
                if (!_excludedLogged.Add(path)) return;
            }
            TranslationPlugin.LogInfo("Skipping excluded text object: " + path);
        }

        private static void WarnOnce(string key, Exception ex)
        {
            lock (_warned)
            {
                if (!_warned.Add(key)) return;
            }
            TranslationPlugin.LogWarning("Text hook error (" + key + "): " + ex);
        }
    }
}
