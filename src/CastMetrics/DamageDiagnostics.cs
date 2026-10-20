using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CastMetrics;

/// <summary>
/// EXPERIMENTAL, passive damage-source identification for future DPS.
/// Neither HP changes nor combat-number popups are proof of damage ownership.
/// This class never feeds the actual HUD and never changes game state.
/// </summary>
internal static class DamageDiagnostics
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
    private static Harmony? _harmony;

    private static int _hpEvents;
    private static int _numberEvents;
    private static int _numberTotal;
    private static readonly Dictionary<string, int> TextKinds = new();
    private static readonly List<string> HpSamples = new();
    private static readonly List<string> NumberSamples = new();

    private static int _reports;
    private static float _nextReport;
    private static bool _hpInstalled;
    private static bool _numberInstalled;

    internal static void Install()
    {
        _harmony = new Harmony("com.horizon.castmetrics.damage.diagnostics");
        bool isCommunity = FindType("MagicalActual.PlayerCharacter") != null;
        string playerType = isCommunity ? "MagicalActual.PlayerCharacter" : "cdc";
        string hpMethod = isCommunity ? "MonsterHitpointsChanged" : "lem";
        string overlayType = isCommunity
            ? "EckTechGames.FloatingCombatText.OverlayCanvasController" : "cuc";
        string overlayMethod = isCommunity ? "ShowCombatText" : "oih";

        Type? player = FindType(playerType);
        Type? overlay = FindType(overlayType);
        MethodInfo? hpTarget = player?.GetMethods(Flags).FirstOrDefault(m =>
            m.Name == hpMethod && !m.IsStatic && m.ReturnType == typeof(void) &&
            m.GetParameters().Length == 3 &&
            m.GetParameters().All(p => p.ParameterType == typeof(int)));
        // Use the numeric combat-text overload only (monster, combat-text
        // category enum, integer). String popups are unrelated.
        MethodInfo? textTarget = overlay?.GetMethods(Flags).FirstOrDefault(m =>
            m.Name == overlayMethod && !m.IsStatic &&
            m.ReturnType == typeof(void) && m.GetParameters().Length == 3 &&
            m.GetParameters()[2].ParameterType == typeof(int));

        _hpInstalled = TryHook(hpTarget, nameof(AfterMonsterHp), playerType + "." + hpMethod);
        _numberInstalled = TryHook(textTarget, nameof(AfterCombatNumber), overlayType + "." + overlayMethod);
        Plugin.Logger.LogInfo("[CastMetrics] DPS diagnostics initialized (edition=" +
                              (isCommunity ? "Community" : "Spades") +
                              ", HP hook=" + _hpInstalled +
                              ", floating number hook=" + _numberInstalled +
                              "). Values are NOT yet attributed to local attacks.");
    }

    private static bool TryHook(MethodInfo? target, string callbackName, string label)
    {
        if (target == null)
        {
            Plugin.Logger.LogWarning("[CastMetrics] DPS probe signature missing: " + label);
            return false;
        }
        MethodInfo? callback = typeof(DamageDiagnostics).GetMethod(callbackName, Flags);
        if (callback == null) return false;
        try
        {
            _harmony!.Patch(target, postfix: new HarmonyMethod(callback));
            Plugin.Logger.LogInfo("[CastMetrics] Read-only DPS probe installed: " + label);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[CastMetrics] DPS probe " + label + " unavailable: " +
                                      ex.GetType().Name + " " + ex.Message);
            return false;
        }
    }

    // The three ints are recorded as raw values until we confirm their
    // meaning. They do not contain a known damage-source/player ID.
    private static void AfterMonsterHp(int __0, int __1, int __2)
    {
        try
        {
            if (_hpEvents < 1000000) _hpEvents++;
            if (HpSamples.Count < 5)
                HpSamples.Add("(" + __0 + "," + __1 + "," + __2 + ")");
        }
        catch { }
    }

    // Harmony boxes the original CombatTextType enum as __1. We sample the
    // category alongside the numeric popup, but DO NOT assume it is our DPS.
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
            // Cumulative signed total is a diagnostic only, not calculated DPS.
            if (_numberTotal < 100000000 && __2 > 0 && __2 < 10000000)
                _numberTotal += __2;
        }
        catch { }
    }

    internal static void Report(float now)
    {
        if (_reports >= 30 || now < _nextReport || (!_hpInstalled && !_numberInstalled))
            return;

        _nextReport = now + 10f;
        _reports++;
        Plugin.Logger.LogInfo("[CastMetrics] DPS source probe last 10s: " +
            "HP events=" + _hpEvents +
            " rawSamples=[" + string.Join(", ", HpSamples) + "]; " +
            "floatingNumbers=" + _numberEvents +
            " categories=[" + string.Join(", ", TextKinds.Select(kv => kv.Key + "=" + kv.Value)) + "]" +
            " rawSamples=[" + string.Join(", ", NumberSamples) + "]" +
            ". Attribution NOT established.");
        _hpEvents = 0;
        _numberEvents = 0;
        _numberTotal = 0;
        HpSamples.Clear();
        NumberSamples.Clear();
        TextKinds.Clear();
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
