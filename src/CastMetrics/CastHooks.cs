using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CastMetrics;

/// <summary>
/// Read-only Runemage casting diagnostics. Successful rune recognition is
/// observed at prepareSpellCast(int), NOT finishSpellCast() (every gesture).
/// This is not a network-confirmed spell hit or damage event.
/// </summary>
internal static class CastHooks
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
    // Community spellIdx enum lists 28 spells followed by NONE.
    // Some invalid IDs may be negative, so reject those as well.
    private const int NoneSpellId = 28;

    private static Harmony? _harmony;
    internal static bool HasVerifiedHook { get; private set; } // Installed, pending gameplay validation.
    internal static bool HasObservedCast { get; private set; }
    internal static bool IsExperimentalSpades { get; private set; }

    private static readonly Dictionary<string, int> ProbeTotals = new();
    private static readonly Dictionary<string, int> ProbeLocals = new();
    private static readonly Dictionary<int, int> RecognizedIds = new();
    private static float _nextProbeReport;
    private static int _probeReports;
    private static int _preparedRaw;
    private static int _preparedLocal;
    private static int _preparedInvalid;
    private static bool _warnedOwnership;
    private static bool _loggedFirstRecognition;

    internal static void Install()
    {
        _harmony = new Harmony("com.horizon.castmetrics.casts");
        Type? community = FindType("MagicalActual.Runemage");
        if (community != null)
        {
            Plugin.Logger.LogInfo("[CastMetrics] Community Runemage found. Counting prepared spells, not finish gestures.");
            InstallPreparationHook(community, "prepareSpellCast", "Community");
            return;
        }

        IsExperimentalSpades = true;
        Type? spades = FindType("cgk");
        if (spades == null)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Spades Runemage cgk missing; no spell recognition hook.");
            return;
        }

        // Mappings verified structurally against supplied interop and observed
        // in gameplay as cgk.lsa=attempts, cgk.lsk=prepared spells:
        // Community finishSpellCast()    -> cgk.lsa()
        // Community prepareSpellCast(int) -> cgk.lsk(int)
        // Community cancelPreparedSpell() -> cgk.lsm()
        // Community castPreparedSpell()   -> cgk.lso()
        InstallPreparationHook(spades, "lsk", "Spades experimental");
        InstallSpadesProbes(spades);

        if (Plugin.SpadesCastType.Value.Trim().Length > 0 ||
            Plugin.SpadesCastMethod.Value.Trim().Length > 0)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Older manual casting-method override is ignored. " +
                                     "It could otherwise count invalid drawing gestures.");
        }
    }

    private static void InstallPreparationHook(Type controller, string methodName, string label)
    {
        MethodInfo? target = controller.GetMethods(Flags).FirstOrDefault(m =>
            m.Name == methodName && !m.IsStatic &&
            m.ReturnType == typeof(void) &&
            m.GetParameters().Length == 1 &&
            m.GetParameters()[0].ParameterType == typeof(int));
        MethodInfo? callback = typeof(CastHooks).GetMethod(nameof(AfterPreparedSpell),
            BindingFlags.NonPublic | BindingFlags.Static);
        if (target == null || callback == null)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Spell preparation signature unavailable: " +
                                      controller.FullName + "." + methodName + "(int)");
            return;
        }

        try
        {
            _harmony!.Patch(target, postfix: new HarmonyMethod(callback));
            HasVerifiedHook = true;
            Plugin.Logger.LogInfo("[CastMetrics] Installed " + label + " recognized-spell postfix: " +
                                  controller.FullName + "." + methodName + "(int). " +
                                  "Only eligible local spell IDs count; attempts do not.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Spell preparation hook rejected: " +
                                      ex.GetType().Name + ": " + ex.Message);
        }
    }

    // Harmony's __0 corresponds to the method's first explicit argument
    // (recognized spell ID), not its controller instance.
    private static void AfterPreparedSpell(object __instance, int __0)
    {
        try
        {
            _preparedRaw++;
            if (!Plugin.Enabled.Value) return;
            if (!BelongsToLocalPlayer(__instance))
            {
                if (!_warnedOwnership)
                {
                    _warnedOwnership = true;
                    Plugin.Logger.LogInfo("[CastMetrics] Prepared-spell event rejected as non-local: " +
                                          InstanceDescription(__instance));
                }
                return;
            }

            _preparedLocal++;
            if (RecognizedIds.TryGetValue(__0, out int count))
            {
                if (count < 1000000) RecognizedIds[__0] = count + 1;
            }
            else if (RecognizedIds.Count < 24)
            {
                RecognizedIds[__0] = 1;
            }

            // Unknown or NONE spells are not successful rune recognition.
            if (__0 < 0 || __0 == NoneSpellId)
            {
                _preparedInvalid++;
                return;
            }

            HasObservedCast = true;
            CastWindow.Record(Time.realtimeSinceStartup);
            if (!_loggedFirstRecognition)
            {
                _loggedFirstRecognition = true;
                Plugin.Logger.LogInfo("[CastMetrics] First LOCAL recognized spell: ID=" + __0 +
                    ", hook=" + (IsExperimentalSpades ? "cgk.lsk(int)" : "Runemage.prepareSpellCast(int)") +
                    ". Please compare successful and failed rune drawings in-game.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Spell-recognition observation failed: " +
                                     ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static bool BelongsToLocalPlayer(object instance)
    {
        if (instance is not Component component) return false;
        Transform? node = component.transform;
        for (int i = 0; node != null && i < 40; i++, node = node.parent)
            if (string.Equals(node.name, "PlayerRigv4", StringComparison.OrdinalIgnoreCase))
                return true;
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
        // Compared with cgk.lsk (now the counting hook), lsa is a drawing
        // attempt; lsm is cancellation and lso a subsequent casting stage.
        // Avoid high-frequency FixedUpdate-like probes from previous builds.
        string[] methods = { "lsa", "lsm", "lso" };
        MethodInfo? callback = typeof(CastHooks).GetMethod(nameof(OnProbe),
            BindingFlags.NonPublic | BindingFlags.Static);
        if (callback == null) return;
        foreach (string name in methods)
        {
            MethodInfo? target = controller.GetMethods(Flags).FirstOrDefault(m =>
                m.Name == name && !m.IsStatic && m.ReturnType == typeof(void) &&
                m.GetParameters().Length == 0);
            if (target == null) continue;

            try
            {
                _harmony!.Patch(target, postfix: new HarmonyMethod(callback));
                ProbeTotals[name] = 0;
                ProbeLocals[name] = 0;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("[CastMetrics] Skipped casting diagnostic cgk." + name +
                                         ": " + ex.GetType().Name);
            }
        }
        Plugin.Logger.LogInfo("[CastMetrics] Read-only Spades diagnostics: " +
                              string.Join(", ", ProbeTotals.Keys) +
                              "; cgk.lsk(int) is the only counted method.");
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
        if (!IsExperimentalSpades || _probeReports >= 30 || now < _nextProbeReport)
            return;
        _nextProbeReport = now + 10f;
        _probeReports++;

        string stages = string.Join(", ", ProbeTotals.Select(kv =>
            "cgk." + kv.Key + "=" + kv.Value + " total/" +
            (ProbeLocals.TryGetValue(kv.Key, out int local) ? local : 0) + " local"));
        string ids = string.Join(", ", RecognizedIds.OrderBy(kv => kv.Key)
            .Select(kv => kv.Key + "=" + kv.Value));

        Plugin.Logger.LogInfo("[CastMetrics] Recognized-spell probe last 10s: " +
            stages + "; cgk.lsk=" + _preparedRaw + " total/" + _preparedLocal +
            " local (" + _preparedInvalid + " invalid); IDs=[" + ids + "]");
        foreach (string key in ProbeTotals.Keys.ToArray())
        {
            ProbeTotals[key] = 0;
            ProbeLocals[key] = 0;
        }
        RecognizedIds.Clear();
        _preparedRaw = 0;
        _preparedLocal = 0;
        _preparedInvalid = 0;
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
