using System;
using System.Collections.Generic;

namespace CastMetrics;

/// <summary>Only locally recognized Runemage spells enter this clock. Failed drawing gestures and damage are excluded.</summary>
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

    internal static float CastsPerSecond(float now, float seconds)
    {
        seconds = Math.Clamp(seconds, 1f, 15f);
        Trim(now, seconds);
        return Casts.Count / seconds;
    }
}
