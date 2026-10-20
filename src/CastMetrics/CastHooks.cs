using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CastMetrics;

/// <summary>
/// Read-only Runemage hooks. Spades cgk maps to MagicalActual.Runemage via
/// multiple uncommon method signatures; the cast event itself is experimental.
/// </summary>
internal static class CastHooks
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;

    private static Harmony? _harmony;
    internal static bool HasVerifiedHook { get; private set; } // Means hook installed, not event validated.
    internal static bool HasObservedCast { get; private set; }
    internal static bool IsExperimentalSpades { get; private set; }

    private static readonly Dictionary<string, int> ProbeTotals = new();
    private static readonly Dictionary<string, int> ProbeLocals = new();
    private static float _nextProbeReport;
    private static int _probeReports;
    private static int _rawCastCallbacks;
    private static int _localCastCallbacks;
    private static bool _warnedOwnership;
    private static bool _loggedFirstCast;

    internal static void Install()
    {
        _harmony = new Harmony("com.horizon.castmetrics.casts");
        Type? community = FindType("MagicalActual.Runemage");
        if (community != null)
        {
            Plugin.Logger.LogInfo("[CastMetrics] Community Runemage found.");
            InstallCastHook(community, "finishSpellCast", "Community Runemage");
            return;
        }

        IsExperimentalSpades = true;

        // Concrete matches from the supplied Spades/Community IL2CPP interop:
        // Community Runemage.DLSDFPASFD(ushort, Vector3, int) -> cgk.lrd
        // Community Runemage.shootArrow(Vector3, Vector3, int, int) -> cgk.lsn
        // Community Runemage.getRelativePosition(Transform, Vector3) -> cgk.lsb
        // The zero-arg void immediately before getRelativePosition is
        // Community finishSpellCast -> Spades cgk.lsa (not yet live verified).
        Type? spadesRunemage = FindType("cgk");
        if (spadesRunemage == null)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Spades Runemage cgk not found; cast HUD disabled.");
            return;
        }

        Plugin.Logger.LogInfo("[CastMetrics] Spades Runemage cgk found. " +
                              "Trying structurally mapped cgk.lsa cast-completion method.");
        InstallCastHook(spadesRunemage, "lsa", "Spades Runemage (experimental)");

        // Limit diagnostics to casting-path neighbours. All are read-only,
        // capped counters; none are counted as completed spells.
        InstallSpadesProbes(spadesRunemage);

        string manualType = Plugin.SpadesCastType.Value.Trim();
        string manualMethod = Plugin.SpadesCastMethod.Value.Trim();
        if (manualType.Length > 0 && manualMethod.Length > 0 &&
            !(manualType == "cgk" && manualMethod == "lsa"))
        {
            Type? controller = FindType(manualType);
            if (controller != null)
            {
                // Manual hook is diagnostic too, and may duplicate events.
                Plugin.Logger.LogWarning("[CastMetrics] Manual experimental cast override enabled: " +
                                          manualType + "." + manualMethod);
                InstallCastHook(controller, manualMethod, "Spades manual experiment");
            }
            else
            {
                Plugin.Logger.LogWarning("[CastMetrics] Configured experimental type missing: " +
                                          manualType);
            }
        }
    }

    private static void InstallCastHook(Type controller, string methodName, string label)
    {
        MethodInfo? target = controller.GetMethods(Flags).FirstOrDefault(m =>
            m.Name == methodName && !m.IsStatic &&
            m.ReturnType == typeof(void) && m.GetParameters().Length == 0);
        MethodInfo? callback = typeof(CastHooks).GetMethod(nameof(AfterCast),
            BindingFlags.NonPublic | BindingFlags.Static);
        if (target == null || callback == null)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Cast signature unavailable: " +
                                      controller.FullName + "." + methodName + "()");
            return;
        }
        try
        {
            _harmony!.Patch(target, postfix: new HarmonyMethod(callback));
            HasVerifiedHook = true;
            Plugin.Logger.LogInfo("[CastMetrics] Installed " + label + " postfix: " +
                                  controller.FullName + "." + methodName + "()");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Cast hook rejected: " +
                                      ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void AfterCast(object __instance)
    {
        try
        {
            _rawCastCallbacks++;
            if (!Plugin.Enabled.Value) return;
            if (!BelongsToLocalPlayer(__instance))
            {
                if (!_warnedOwnership)
                {
                    _warnedOwnership = true;
                    Plugin.Logger.LogInfo("[CastMetrics] Cast hook fired but was rejected as non-local. " +
                                          InstanceDescription(__instance));
                }
                return;
            }

            _localCastCallbacks++;
            HasObservedCast = true;
            CastWindow.Record(Time.realtimeSinceStartup);
            if (!_loggedFirstCast)
            {
                _loggedFirstCast = true;
                Plugin.Logger.LogInfo("[CastMetrics] First local casting callback received via " +
                    (IsExperimentalSpades ? "cgk.lsa (EXPERIMENTAL)" : "Runemage.finishSpellCast") +
                    ". Confirm each completed Fireball produces one callback.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Callback observation failed: " +
                                     ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static bool BelongsToLocalPlayer(object instance)
    {
        if (instance is not Component component) return false;
        Transform? node = component.transform;
        for (int i = 0; node != null && i < 40; i++, node = node.parent)
        {
            if (string.Equals(node.name, "PlayerRigv4", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string InstanceDescription(object instance)
    {
        if (instance is not Component component)
            return "Managed type=" + (instance?.GetType().FullName ?? "(null)") +
                   " (not a Unity Component wrapper)";
        string path = component.name;
        Transform? node = component.transform.parent;
        for (int i = 0; node != null && i < 7; i++, node = node.parent)
            path = node.name + "/" + path;
        return "Scene path=" + path + " Type=" + instance.GetType().FullName;
    }

    private static void InstallSpadesProbes(Type controller)
    {
        string[] methods = { "lro", "lrp", "lrs", "lru", "lrw", "lrx", "lry", "lrz",
                             "lsk", "lsl", "lsm", "lso", "lsp" };
        MethodInfo? callback = typeof(CastHooks).GetMethod(nameof(OnProbe),
            BindingFlags.NonPublic | BindingFlags.Static);
        if (callback == null) return;
        foreach (string name in methods)
        {
            MethodInfo? target = controller.GetMethods(Flags).FirstOrDefault(m =>
                m.Name == name && !m.IsStatic && m.ReturnType == typeof(void) &&
                (m.GetParameters().Length == 0 ||
                 (name == "lsk" && m.GetParameters().Length == 1 &&
                  m.GetParameters()[0].ParameterType == typeof(int))));
            if (target == null) continue;
            try
            {
                _harmony!.Patch(target, postfix: new HarmonyMethod(callback));
                ProbeTotals[name] = 0;
                ProbeLocals[name] = 0;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[CastMetrics] Cannot inspect cgk." + name +
                                         ": " + ex.GetType().Name);
            }
        }
        Plugin.Logger.LogInfo("[CastMetrics] Spades cgk read-only cast-path probes: " +
                              string.Join(", ", ProbeTotals.Keys) +
                              ". Counts reported only every 10 seconds.");
    }

    private static void OnProbe(object __instance, MethodBase __originalMethod)
    {
        try
        {
            if (__originalMethod == null) return;
            string name = __originalMethod.Name;
            if (ProbeTotals.TryGetValue(name, out int total) && total < 1000000)
                ProbeTotals[name] = total + 1;
            if (BelongsToLocalPlayer(__instance) &&
                ProbeLocals.TryGetValue(name, out int local) && local < 1000000)
                ProbeLocals[name] = local + 1;
        }
        catch { }
    }

    internal static void ReportProbeSnapshot(float now)
    {
        if (!IsExperimentalSpades || _probeReports >= 30 ||
            now < _nextProbeReport) return;
        _nextProbeReport = now + 10f;
        _probeReports++;

        string summary = string.Join(", ", ProbeTotals.Select(kv =>
            kv.Key + "=" + kv.Value + " total/" +
            (ProbeLocals.TryGetValue(kv.Key, out int local) ? local : 0) + " local"));
        Plugin.Logger.LogInfo("[CastMetrics] cgk probe last 10s: " + summary +
            "; cgk.lsa callbacks=" + _rawCastCallbacks +
            " total/" + _localCastCallbacks + " local");
        foreach (string key in ProbeTotals.Keys.ToArray())
        {
            ProbeTotals[key] = 0;
            ProbeLocals[key] = 0;
        }
        _rawCastCallbacks = 0;
        _localCastCallbacks = 0;
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            catch { }
        }
        return null;
    }
}
