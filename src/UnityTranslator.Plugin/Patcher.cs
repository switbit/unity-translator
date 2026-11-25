using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnityTranslator
{
    internal static class Patcher
    {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static int Install(Harmony harmony)
        {
            int patched = 0;

            patched += TryPatchProperty(harmony, typeof(Text), "text", "LegacySet", null);
            patched += TryPatch(harmony, AccessTools.Method(typeof(Text), "OnEnable", Type.EmptyTypes),
                                null, "LegacyOnEnable");
            patched += TryPatchProperty(harmony, typeof(TextMesh), "text", "TextMeshSet", null);

            if (TmpAvailable())
            {
                patched += InstallTmp(harmony);
            }

            return patched;
        }

        /// <summary>
        /// Checks for TextMeshPro without referencing its types, so the plugin still
        /// loads in games that were built without the TMP package.
        /// </summary>
        private static bool TmpAvailable()
        {
            try
            {
                if (Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") != null) return true;
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    try
                    {
                        if (assemblies[i].GetType("TMPro.TMP_Text", false) != null) return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int InstallTmp(Harmony harmony)
        {
            int patched = 0;
            patched += TryPatchProperty(harmony, typeof(TMP_Text), "text", "TmpSet", null);

            MethodInfo[] methods = typeof(TMP_Text).GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                               BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "SetText" || method.IsAbstract) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0) continue;

                if (parameters[0].ParameterType == typeof(string))
                {
                    patched += TryPatch(harmony, method, "TmpSetText", null);
                }
                else if (parameters[0].ParameterType == typeof(System.Text.StringBuilder) &&
                         parameters.Length == 1)
                {
                    patched += TryPatch(harmony, method, "TmpSetTextBuilder", null);
                }
            }

            patched += TryPatch(harmony, AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable", Type.EmptyTypes),
                                null, "TmpOnEnableUGUI");
            patched += TryPatch(harmony, AccessTools.Method(typeof(TextMeshPro), "OnEnable", Type.EmptyTypes),
                                null, "TmpOnEnable3D");

            return patched;
        }

        private static int TryPatchProperty(Harmony harmony, Type type, string property, string prefix, string postfix)
        {
            MethodInfo method = AccessTools.PropertySetter(type, property);
            if (method == null)
            {
                TranslationPlugin.LogWarning("Cannot find property setter " + type.Name + "." + property);
                return 0;
            }
            return TryPatch(harmony, method, prefix, postfix);
        }

        private static int TryPatch(Harmony harmony, MethodInfo target, string prefix, string postfix)
        {
            if (target == null) return 0;
            try
            {
                HarmonyMethod prefixMethod = prefix == null ? null : MakeHarmonyMethod(prefix);
                HarmonyMethod postfixMethod = postfix == null ? null : MakeHarmonyMethod(postfix);
                if (prefixMethod == null && postfixMethod == null) return 0;
                harmony.Patch(target, prefix: prefixMethod, postfix: postfixMethod);
                return 1;
            }
            catch (Exception ex)
            {
                TranslationPlugin.LogWarning("Could not patch " + target.DeclaringType.Name + "." + target.Name +
                                             ": " + ex.Message);
                return 0;
            }
        }

        private static HarmonyMethod MakeHarmonyMethod(string name)
        {
            MethodInfo method = typeof(TextHooks).GetMethod(name, StaticFlags);
            if (method == null) throw new MissingMethodException("TextHooks." + name);
            return new HarmonyMethod(method);
        }
    }
}
