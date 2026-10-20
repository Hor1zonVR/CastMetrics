using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CastMetrics;

/// <summary>
/// Read-only Harmony posthook. Does not change cast arguments, cooldowns or game state.
/// Community has human-readable method names; Spades requires explicitly verified names.
/// </summary>
internal static class CastHooks
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
    private static Harmony? _harmony;
    private static Transform? _lastWand;
    internal static Transform? LastWand => _lastWand;
    internal static bool HasVerifiedHook { get; private set; }

    internal static void Install()
    {
        Type? community = FindType("MagicalActual.Runemage");
        if (community != null)
        {
            InstallKnown(community, "finishSpellCast", "Community");
            return;
        }

        Plugin.Logger.LogWarning("[CastMetrics] Community Runemage not found. " +
            "Spades has obfuscated names, so automatic cast counting remains disabled until confirmed.");
        string typeName = Plugin.SpadesCastType.Value.Trim();
        string methodName = Plugin.SpadesCastMethod.Value.Trim();
        if (!string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(methodName))
        {
            Type? candidate = FindType(typeName);
            if (candidate != null)
                InstallKnown(candidate, methodName, "Spades MANUAL EXPERIMENT");
            else
                Plugin.Logger.LogWarning("[CastMetrics] Configured Spades cast type was not found: " + typeName);
        }
        else
        {
            // Bounded, non-invasive information to help match Spades interop in a future update.
            Type? probable = FindType("bnv");
            if (probable != null)
            {
                var names = probable.GetMethods(Flags)
                    .Where(m => !m.IsStatic && m.ReturnType == typeof(void) &&
                                m.GetParameters().Length == 0 && !m.IsSpecialName)
                    .Select(m => m.Name).Distinct().Take(30);
                Plugin.Logger.LogInfo("[CastMetrics] Spades candidate bnv zero-arg methods (NOT confirmed): " +
                                      string.Join(", ", names));
            }
        }
    }

    private static void InstallKnown(Type controller, string name, string edition)
    {
        MethodInfo? target = controller.GetMethods(Flags)
            .FirstOrDefault(m => m.Name == name && !m.IsStatic &&
                                 m.ReturnType == typeof(void) &&
                                 m.GetParameters().Length == 0);
        MethodInfo? callback = typeof(CastHooks).GetMethod(nameof(AfterCast), BindingFlags.NonPublic | BindingFlags.Static);
        if (target == null || callback == null)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Casting signature missing in " + edition + ": " +
                                      controller.FullName + "." + name + "()");
            return;
        }
        try
        {
            _harmony = new Harmony("com.horizon.castmetrics.cast");
            _harmony.Patch(target, postfix: new HarmonyMethod(callback));
            HasVerifiedHook = true;
            Plugin.Logger.LogInfo("[CastMetrics] Read-only " + edition + " posthook installed: " +
                                  controller.FullName + "." + name + "()");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Hook rejected; no casting data will be shown: " +
                                      ex.GetType().Name + " " + ex.Message);
        }
    }

    private static void AfterCast(object __instance)
    {
        try
        {
            if (!Plugin.Enabled.Value) return;
            // A Harmony call is not proof this belongs to the local player.
            // This first build is explicitly a cast-event probe and needs live attribution testing.
            CastWindow.Record(Time.realtimeSinceStartup);
            Transform? wand = GetWand(__instance);
            if (wand != null) _lastWand = wand;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug("[CastMetrics] Posthook observation failed: " + ex.Message);
        }
    }

    private static Transform? GetWand(object instance)
    {
        foreach (string member in new[] { "wandTip", "playerWand", "wand", "wandtip" })
        {
            object? value = Member(instance, member);
            Transform? transform = AsTransform(value);
            if (transform != null) return transform;
            if (value != null)
            {
                transform = AsTransform(Member(value, "wandTip")) ??
                            AsTransform(Member(value, "tip")) ??
                            AsTransform(Member(value, "transform"));
                if (transform != null) return transform;
            }
        }
        return AsTransform(Member(instance, "transform"));
    }

    private static object? Member(object source, string name)
    {
        try
        {
            var type = source.GetType();
            var property = type.GetProperty(name, Flags);
            if (property != null) return property.GetValue(source);
            var field = type.GetField(name, Flags);
            if (field != null) return field.GetValue(source);
            var getter = type.GetMethod("get_" + name, Flags, null, Type.EmptyTypes, null);
            return getter?.Invoke(source, null);
        }
        catch { return null; }
    }

    private static Transform? AsTransform(object? value) =>
        value is Transform transform ? transform :
        value is GameObject go ? go.transform :
        value is Component component ? component.transform : null;

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { Type? type = assembly.GetType(fullName, false); if (type != null) return type; }
            catch { }
        }
        return null;
    }
}
