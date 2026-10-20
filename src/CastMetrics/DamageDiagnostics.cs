using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CastMetrics;

/// <summary>
/// Read-only damage-path investigation for future DPS attribution.
/// These callbacks do not prove who caused a hit and never feed the HUD.
/// </summary>
internal static class DamageDiagnostics
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
    private static Harmony? _harmony;

    private static int _hpEvents;
    private static int _numberEvents;
    private static readonly Dictionary<string, int> TextKinds = new();
    private static readonly Dictionary<string, int> PathEvents = new();
    private static readonly List<string> HpSamples = new();
    private static readonly List<string> NumberSamples = new();

    private static int _reports;
    private static float _nextReport;
    private static bool _hpInstalled;
    private static bool _numberInstalled;

    internal static void Install()
    {
        _harmony = new Harmony("com.horizon.castmetrics.damage.diagnostics");
        bool community = FindType("MagicalActual.PlayerCharacter") != null;
        string playerType = community ? "MagicalActual.PlayerCharacter" : "cdc";
        string hpMethod = community ? "MonsterHitpointsChanged" : "lem";
        string overlayType = community
            ? "EckTechGames.FloatingCombatText.OverlayCanvasController" : "cuc";
        string numberMethod = community ? "ShowCombatText" : "oih";
        string stringMethod = community ? "ShowCombatText" : "oig";
        string anchorType = community
            ? "EckTechGames.FloatingCombatText.CombatTextAnchorController" : "ctz";
        string anchorNumber = community ? "ShowCombatText" : "ohz";
        string anchorString = community ? "ShowCombatText" : "ohy";
        string monsterType = community ? "MagicalActual.Monster" : "bwh";

        Type? player = FindType(playerType);
        Type? overlay = FindType(overlayType);
        MethodInfo? hp = player?.GetMethods(Flags).FirstOrDefault(m =>
            m.Name == hpMethod && !m.IsStatic && m.ReturnType == typeof(void) &&
            m.GetParameters().Length == 3 &&
            m.GetParameters().All(p => p.ParameterType == typeof(int)));
        MethodInfo? numericText = overlay?.GetMethods(Flags).FirstOrDefault(m =>
            m.Name == numberMethod && !m.IsStatic && m.ReturnType == typeof(void) &&
            m.GetParameters().Length == 3 &&
            m.GetParameters()[2].ParameterType == typeof(int));

        _hpInstalled = TryHook(hp, nameof(AfterMonsterHp), playerType + "." + hpMethod);
        _numberInstalled = TryHook(numericText, nameof(AfterCombatNumber),
                                   overlayType + "." + numberMethod + "(int)");

        // Live tests found repeated monster-HP changes but no numeric
        // OverlayCanvasController callbacks. Probe the other concrete rendering
        // paths (including string overloads) without assuming any are DPS.
        TryObserve(overlayType, stringMethod, typeof(string), "overlay-text");
        TryObserve(anchorType, anchorNumber, typeof(int), "anchor-number");
        TryObserve(anchorType, anchorString, typeof(string), "anchor-text");
        TryObserve(monsterType, "Hit", null, "monster-hit");

        Plugin.Logger.LogInfo("[CastMetrics] DPS source investigation: edition=" +
                              (community ? "Community" : "Spades") +
                              ", HP hook=" + _hpInstalled +
                              ", overlay numeric=" + _numberInstalled +
                              ", extra paths=[" + string.Join(", ", PathEvents.Keys) +
                              "]. No damage is attributed to the local player yet.");
    }

    private static bool TryHook(MethodInfo? target, string callbackName, string label)
    {
        if (target == null)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Damage probe signature missing: " + label);
            return false;
        }
        MethodInfo? callback = typeof(DamageDiagnostics).GetMethod(callbackName, Flags);
        if (callback == null) return false;
        try
        {
            _harmony!.Patch(target, postfix: new HarmonyMethod(callback));
            Plugin.Logger.LogInfo("[CastMetrics] Read-only damage source probe: " + label);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Damage probe " + label + " unavailable: " +
                                      ex.GetType().Name + " " + ex.Message);
            return false;
        }
    }

    private static void TryObserve(string typeName, string methodName,
                                   Type? lastArgumentType, string label)
    {
        Type? type = FindType(typeName);
        MethodInfo? target = type?.GetMethods(Flags).FirstOrDefault(m =>
        {
            if (m.Name != methodName || m.IsStatic ||
                m.ReturnType != typeof(void)) return false;
            ParameterInfo[] pars = m.GetParameters();
            return lastArgumentType == null ? pars.Length == 0 :
                   pars.Length == 3 &&
                   pars[2].ParameterType == lastArgumentType;
        });
        if (target == null)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Extra damage path unavailable: " +
                                     typeName + "." + methodName);
            return;
        }
        MethodInfo? callback = typeof(DamageDiagnostics).GetMethod(nameof(OnPathEvent), Flags);
        if (callback == null) return;
        try
        {
            _harmony!.Patch(target, postfix: new HarmonyMethod(callback));
            PathEvents[label] = 0;
            Plugin.Logger.LogInfo("[CastMetrics] Passive path probe installed: " +
                                  typeName + "." + methodName + " => " + label);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Extra probe " + label +
                                      " could not attach: " + ex.GetType().Name);
        }
    }

    private static void OnPathEvent(MethodBase __originalMethod)
    {
        try
        {
            if (__originalMethod == null) return;
            string name = __originalMethod.Name;
            string declaring = __originalMethod.DeclaringType?.FullName ?? "";

            string key;
            if (name == "Hit") key = "monster-hit";
            else if (name == "oig" || (name == "ShowCombatText" &&
                     declaring.Contains("OverlayCanvasController")))
                key = "overlay-text";
            else if (name == "ohz") key = "anchor-number";
            else if (name == "ohy") key = "anchor-text";
            else if (name == "ShowCombatText" &&
                     declaring.Contains("CombatTextAnchorController"))
            {
                Type type = ((MethodInfo)__originalMethod).GetParameters()[2].ParameterType;
                key = type == typeof(int) ? "anchor-number" : "anchor-text";
            }
            else return;

            if (PathEvents.TryGetValue(key, out int count) && count < 1000000)
                PathEvents[key] = count + 1;
        }
        catch { }
    }

    private static void AfterMonsterHp(int __0, int __1, int __2)
    {
        try
        {
            if (_hpEvents < 1000000) _hpEvents++;
            if (HpSamples.Count < 7)
                HpSamples.Add(Time.realtimeSinceStartup.ToString("0.0") +
                              "s:(" + __0 + "," + __1 + "," + __2 + ")");
        }
        catch { }
    }

    private static void AfterCombatNumber(object __1, int __2)
    {
        try
        {
            if (_numberEvents < 1000000) _numberEvents++;
            string kind = __1 == null ? "(null)" : __1.ToString() ?? "(unknown)";
            if (kind.Length > 40) kind = kind.Substring(0, 40);
            if (TextKinds.TryGetValue(kind, out int count))
                TextKinds[kind] = count + 1;
            else if (TextKinds.Count < 12)
                TextKinds[kind] = 1;
            if (NumberSamples.Count < 8)
                NumberSamples.Add(kind + ":" + __2);
        }
        catch { }
    }

    internal static void Report(float now)
    {
        if (_reports >= 30 || now < _nextReport ||
            (!_hpInstalled && !_numberInstalled && PathEvents.Count == 0))
            return;
        _nextReport = now + 10f;
        _reports++;

        string paths = string.Join(", ", PathEvents.Select(kv => kv.Key + "=" + kv.Value));
        Plugin.Logger.LogInfo("[CastMetrics] DPS source probe last 10s: " +
            "HP events=" + _hpEvents +
            " rawSamples=[" + string.Join(", ", HpSamples) + "]; " +
            "floatingNumbers=" + _numberEvents +
            " categories=[" + string.Join(", ", TextKinds.Select(kv => kv.Key + "=" + kv.Value)) + "]" +
            " rawSamples=[" + string.Join(", ", NumberSamples) + "]" +
            " otherPaths=[" + paths + "]. Local damage attribution unverified.");

        _hpEvents = 0;
        _numberEvents = 0;
        HpSamples.Clear();
        NumberSamples.Clear();
        TextKinds.Clear();

        foreach (string key in PathEvents.Keys.ToArray())
            PathEvents[key] = 0;
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
