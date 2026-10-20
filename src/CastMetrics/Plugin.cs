using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace CastMetrics;

[BepInPlugin("com.horizon.castmetrics", "CastMetrics", "0.1.5")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<bool> ShowCasts = null!;
    internal static ConfigEntry<bool> ShowDps = null!;
    internal static ConfigEntry<bool> HideWhenIdle = null!;
    internal static ConfigEntry<float> WindowSeconds = null!;
    internal static ConfigEntry<float> IdleSeconds = null!;
    internal static ConfigEntry<float> Scale = null!;
    internal static ConfigEntry<float> OffsetX = null!;
    internal static ConfigEntry<float> OffsetY = null!;
    internal static ConfigEntry<float> OffsetZ = null!;
    internal static ConfigEntry<bool> DebugPreviewAtHeadset = null!;
    internal static ConfigEntry<string> SpadesCastType = null!;
    internal static ConfigEntry<string> SpadesCastMethod = null!;

    public override void Load()
    {
        Logger = Log;
        Enabled = Config.Bind("General", "Enabled", true, "Enable the floating CastMetrics HUD.");
        ShowCasts = Config.Bind("Display", "ShowCastsPerSecond", true, "Show recognized Runemage spell preparations per second (not missed drawing gestures).");
        ShowDps = Config.Bind("Display", "ShowDps", false, "DPS remains hidden by default until player-specific damage is proven. Enabling shows an em dash, never invented damage.");
        HideWhenIdle = Config.Bind("Display", "HideWhenIdle", false, "Hide the HUD after IdleSeconds without any verified casts.");
        WindowSeconds = Config.Bind("Metrics", "RollingWindowSeconds", 5f,
            new ConfigDescription("Cast-rate calculation window.", new AcceptableValueRange<float>(1f, 15f)));
        IdleSeconds = Config.Bind("Display", "IdleSeconds", 8f,
            new ConfigDescription("Seconds before hiding an idle HUD.", new AcceptableValueRange<float>(1f, 30f)));
        Scale = Config.Bind("Display", "Scale", 1f,
            new ConfigDescription("HUD size multiplier.", new AcceptableValueRange<float>(0.25f, 3f)));
        OffsetX = Config.Bind("Position", "RightMeters", 0f,
            new ConfigDescription("Horizontal position relative to the wand tip.", new AcceptableValueRange<float>(-0.5f, 0.5f)));
        OffsetY = Config.Bind("Position", "UpMeters", 0.13f,
            new ConfigDescription("Vertical position relative to the wand tip.", new AcceptableValueRange<float>(-0.5f, 0.5f)));
        OffsetZ = Config.Bind("Position", "ForwardMeters", 0f,
            new ConfigDescription("Depth position relative to the wand tip.", new AcceptableValueRange<float>(-0.5f, 0.5f)));
        DebugPreviewAtHeadset = Config.Bind("Diagnostics", "PreviewAtHeadsetWithoutWand", false,
            "For testing HUD rendering only: show the HUD in front of the headset when no verified local wand tip is available.");
        SpadesCastType = Config.Bind("ExperimentalSpades", "CastControllerFullName", "",
            "Do not guess. Full managed type name, only after verifying the obfuscated Runemage class.");
        SpadesCastMethod = Config.Bind("ExperimentalSpades", "SuccessfulCastMethod", "",
            "Do not guess. Legacy manual override is ignored; only recognized spells are counted.");

        CastHooks.Install();
        DamageDiagnostics.Install();
        AddComponent<CastMetricsBehaviour>();
        Log.LogInfo("[CastMetrics] v0.1.5 loaded; Hook installed=" + CastHooks.HasVerifiedHook +
                    ". Counts recognized spells only; damage callbacks are diagnostic-only.");
    }
}
