using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CastMetrics;

/// <summary>
/// Read-only posthooks. Community and legacy Spades wand classes have a
/// matching method order; newer Spades Runemage methods need live validation.
/// Never alter spell arguments, inputs or game timing.
/// </summary>
internal static class CastHooks
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
    private static Harmony? _harmony;
    internal static bool HasVerifiedHook { get; private set; }
    internal static bool HasObservedCast { get; private set; }
    internal static bool IsExperimentalSpades { get; private set; }

    private static readonly Dictionary<string, int> ProbeCalls = new();
    private static readonly Dictionary<string, int> ProbeAllCalls = new();
    private static float _nextProbeReport;
    private static int _probeReports;
    private static bool _warnedLocalCheck;
    private static bool _loggedFirstCast;

    internal static void Install()
    {
        _harmony = new Harmony("com.horizon.castmetrics.casts");
        Type? community = FindType("MagicalActual.Runemage");
        if (community != null)
        {
            InstallKnown(community, "finishSpellCast", "Community Runemage");
            return;
        }

        IsExperimentalSpades = true;
        Plugin.Logger.LogInfo("[CastMetrics] Obfuscated Spades runtime detected.");
        // PlayerWand matches Community's original class member order exactly:
        // Community PlayerWand.finishSpellCast() -> Spades cec.lkk().
        // The match does NOT prove this legacy controller handles modern casts.
        Type? oldWand = FindType("cec");
        if (oldWand != null)
        {
            InstallKnown(oldWand, "lkk", "Spades legacy PlayerWand (experimental)");
        }
        else Plugin.Logger.LogWarning("[CastMetrics] Spades legacy PlayerWand cec unavailable.");

        // Newer Spades Runemage bnv diverges structurally. Do not count
        // its obfuscated methods as spells until test evidence confirms one.
        Type? modern = FindType("bnv");
        if (modern != null) InstallSpadesProbes(modern);

        // Advanced override for explicit, verified game method mappings.
        string type = Plugin.SpadesCastType.Value.Trim();
        string method = Plugin.SpadesCastMethod.Value.Trim();
        if (type.Length != 0 && method.Length != 0)
        {
            Type? controller = FindType(type);
            if (controller != null && !(type == "cec" && method == "lkk"))
                InstallKnown(controller, method, "Spades manual experiment");
            else if (controller == null)
                Plugin.Logger.LogWarning("[CastMetrics] Configured experimental type missing: " + type);
        }
    }

    private static void InstallKnown(Type controller, string name, string edition)
    {
        MethodInfo? target = controller.GetMethods(Flags)
            .FirstOrDefault(m => m.Name == name && !m.IsStatic &&
                                 m.ReturnType == typeof(void) &&
                                 m.GetParameters().Length == 0);
        MethodInfo? callback = typeof(CastHooks).GetMethod(nameof(AfterCast),
            BindingFlags.NonPublic | BindingFlags.Static);
        if (target == null || callback == null)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Zero-argument cast signature missing: " +
                controller.FullName + "." + name);
            return;
        }
        try
        {
            _harmony!.Patch(target, postfix: new HarmonyMethod(callback));
            HasVerifiedHook = true;
            Plugin.Logger.LogInfo("[CastMetrics] Read-only " + edition + " posthook: " +
                                  controller.FullName + "." + name);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Cast posthook unavailable: " +
                                      ex.GetType().Name + " " + ex.Message);
        }
    }

    private static void AfterCast(object __instance)
    {
        try
        {
            if (!Plugin.Enabled.Value) return;
            if (!BelongsToLocalPlayer(__instance))
            {
                if (!_warnedLocalCheck)
                {
                    _warnedLocalCheck = true;
                    Plugin.Logger.LogInfo("[CastMetrics] Cast callback ignored: instance not under local PlayerRigv4.");
                }
                return;
            }
            CastWindow.Record(Time.realtimeSinceStartup);
            HasObservedCast = true;
            if (!_loggedFirstCast)
            {
                _loggedFirstCast = true;
                Plugin.Logger.LogInfo("[CastMetrics] First LOCAL cast callback observed (" +
                    (IsExperimentalSpades ? "Spades experimental" : "Community") +
                    "). Confirm that each successful fireball produces exactly one event.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug("[CastMetrics] Cast callback failed: " + ex.Message);
        }
    }

    private static bool BelongsToLocalPlayer(object instance)
    {
        if (instance is not Component component) return false;
        Transform? node = component.transform;
        for (int i = 0; node != null && i < 35; i++, node = node.parent)
            if (string.Equals(node.name, "PlayerRigv4", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static void InstallSpadesProbes(Type controller)
    {
        // Bounded observational probes on plausible void() methods.
        // Only counters, never one log entry per invocation; no gameplay edits.
        string[] candidates = { "iyw", "iyx", "iyy", "izc", "izd", "ize", "izf", "m" };
        MethodInfo? callback = typeof(CastHooks).GetMethod(nameof(OnProbe),
            BindingFlags.NonPublic | BindingFlags.Static);
        if (callback == null) return;
        foreach (string name in candidates)
        {
            MethodInfo? target = controller.GetMethods(Flags)
                .FirstOrDefault(m => m.Name == name && !m.IsStatic &&
                                     m.ReturnType == typeof(void) &&
                                     m.GetParameters().Length == 0);
            if (target == null) continue;
            try
            {
                _harmony!.Patch(target, postfix: new HarmonyMethod(callback));
                ProbeCalls[name] = 0;
                ProbeAllCalls[name] = 0;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug("[CastMetrics] Skipped Spades probe " +
                    name + ": " + ex.GetType().Name);
            }
        }
        Plugin.Logger.LogInfo("[CastMetrics] Spades bnv passive method probes: " +
            string.Join(", ", ProbeCalls.Keys) +
            ". Reports capped; none count toward casts/s.");
    }

    private static void OnProbe(object __instance, MethodBase __originalMethod)
    {
        try
        {
            if (__originalMethod == null) return;
            string name = __originalMethod.Name;
            if (ProbeAllCalls.TryGetValue(name, out int all) && all < 1000000)
                ProbeAllCalls[name] = all + 1;
            if (!BelongsToLocalPlayer(__instance)) return;
            if (ProbeCalls.TryGetValue(name, out int count) && count < 1000000)
                ProbeCalls[name] = count + 1;
        }
        catch { }
    }

    internal static void ReportProbeSnapshot(float now)
    {
        if (!IsExperimentalSpades || ProbeCalls.Count == 0 ||
            _probeReports >= 24 || now < _nextProbeReport) return;
        _nextProbeReport = now + 10f;
        _probeReports++;
        string counts = string.Join(", ", ProbeCalls.Select(p => p.Key + "=" +
            (ProbeAllCalls.TryGetValue(p.Key, out int all) ? all : 0) + " total / " + p.Value + " local"));
        Plugin.Logger.LogInfo("[CastMetrics] Spades bnv probe hits in past interval: " + counts);
        foreach (string key in ProbeCalls.Keys.ToArray())
        {
            ProbeCalls[key] = 0;
            ProbeAllCalls[key] = 0;
        }
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? t = assembly.GetType(fullName, false);
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }
}
