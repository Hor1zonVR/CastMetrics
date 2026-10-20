using System;
using System.Collections.Generic;

namespace CastMetrics;

/// <summary>
/// Only locally recognized Runemage spells enter this clock. Rate is based
/// on real elapsed time between timestamped casts, never a fixed bucket.
/// </summary>
internal static class CastWindow
{
    private static readonly Queue<float> Casts = new();
    private static float _lastCast = -100f;
    internal static float LastCast => _lastCast;
    private const float BurstGapSeconds = 1.5f;

    internal static void Record(float now)
    {
        if (now < _lastCast || now - _lastCast < 0.065f) return;

        // When a new sequence starts, older spells must not bias its CPS.
        if (now - _lastCast >= BurstGapSeconds)
            Casts.Clear();

        _lastCast = now;
        Casts.Enqueue(now);
        Trim(now, 15f);
    }

    private static void Trim(float now, float seconds)
    {
        while (Casts.Count > 0 && now - Casts.Peek() > seconds)
            Casts.Dequeue();
    }

    internal static float ActiveCastsPerSecond(float now, float configuredWindow)
    {
        if (now < _lastCast || now - _lastCast >= BurstGapSeconds)
            return 0f;

        float window = Math.Clamp(configuredWindow, 1f, 15f);
        Trim(now, window);
        if (Casts.Count == 0) return 0f;

        // One successful cast starts the display, but two are necessary to
        // measure an observed inter-cast cadence.
        if (Casts.Count == 1) return 1f;

        float oldest = Casts.Peek();
        float elapsed = Math.Max(now - oldest, 0.1f);

        // N events have N-1 measured intervals. Using their actual timestamps
        // yields results like 1.29 instead of multiples of 0.5. The elapsed
        // denominator grows between casts, giving a natural, live decline.
        return (Casts.Count - 1) / elapsed;
    }
}
