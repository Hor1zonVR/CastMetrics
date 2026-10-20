using System;
using System.Collections.Generic;

namespace CastMetrics;

/// <summary>
/// Only locally recognized Runemage spells enter this clock. Missing rune
/// recognition does not count. A short inactivity cutoff returns the HUD to 0.
/// </summary>
internal static class CastWindow
{
    private static readonly Queue<float> Casts = new();
    private static float _lastCast = -100f;
    internal static float LastCast => _lastCast;

    internal static void Record(float now)
    {
        if (now < _lastCast || now - _lastCast < 0.065f) return;
        _lastCast = now;
        Casts.Enqueue(now);
        Trim(now, 15f);
    }

    private static void Trim(float now, float window)
    {
        while (Casts.Count > 0 && now - Casts.Peek() > window)
            Casts.Dequeue();
    }

    internal static float ActiveCastsPerSecond(float now, float configuredWindow)
    {
        // The user wants 'Cast/s: 0' when not actively casting. A historical
        // five-second rolling window otherwise leaves stale numbers visible.
        if (now < _lastCast || now - _lastCast >= 1.5f)
            return 0f;

        // Responsive 2s sample, bounded by the existing config for backwards
        // compatibility. No new config prompts / UI needed.
        float window = Math.Clamp(Math.Min(configuredWindow, 2f), 1f, 2f);
        Trim(now, window);
        return Casts.Count / window;
    }
}
