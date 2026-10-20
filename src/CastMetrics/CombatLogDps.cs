using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CastMetrics;

/// <summary>
/// Passive, incremental reader of OrbusVR's own combat.log (the same kind of
/// source consumed by external DPS parsers). The log identifies each attack's
/// recipient, source, amount and timestamp. We never write to this file.
/// </summary>
internal static class CombatLogDps
{
    private static readonly Regex ClassSwitch = new(
        @"^\d\d:\d\d:\d\d:\d{3} \[Combat\] (.+?) switched class: ",
        RegexOptions.CultureInvariant);
    private static readonly Regex DamageLine = new(
        @"^(?<time>\d\d:\d\d:\d\d:\d{3}) \[Combat\] (?<target>.+?) took (?<value>-?\d+) damage from (?<source>.+?)(?: \(Critical\))?$",
        RegexOptions.CultureInvariant);

    private const float MaxEncounterIdle = 12f;
    private const int MaxBytesPerPoll = 65536;
    private const int MaxLineBytes = 4096;

    private static readonly List<byte> Pending = new();
    private static readonly byte[] Buffer = new byte[8192];

    private static string _path = "";
    private static string _playerName = "";
    private static bool _manualName;
    private static bool _filePresent;
    private static long _position;
    private static DateTime _fileCreationUtc;
    private static float _nextPoll;
    private static float _nextWarning;
    private static bool _inCombat;
    private static bool _encounterHasDamage;
    private static double _firstHitLogSeconds;
    private static double _lastHitLogSeconds;
    private static float _lastHitLocalTime;
    private static long _damageTotal;
    private static int _encounters;
    private static int _observedLines;
    private static int _damageEvents;

    internal static bool IsReady => _filePresent && _playerName.Length > 0;
    internal static string PlayerName => _playerName;

    internal static void Install()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string defaultPath = Path.GetFullPath(Path.Combine(localAppData, "..", "LocalLow",
            "Orbus Online, LLC", "OrbusVR", "combat.log"));
        string configuredPath = Plugin.CombatLogPath.Value.Trim().Trim('"');
        _path = configuredPath.Length == 0
            ? defaultPath
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath));

        _playerName = Plugin.DpsCharacterName.Value.Trim();
        _manualName = _playerName.Length > 0;
        Plugin.Logger.LogInfo("[CastMetrics] Reading combat events from the normal OrbusVR LocalLow combat log. " +
                              "Read-only; no external parser required. Path=" + _path);
        if (_manualName)
            Plugin.Logger.LogInfo("[CastMetrics] Character for local DPS configured: " + _playerName);
    }

    internal static void Update(float now)
    {
        if (now < _nextPoll) return;
        _nextPoll = now + 0.2f;

        try
        {
            if (!File.Exists(_path))
            {
                _filePresent = false;
                if (now >= _nextWarning)
                {
                    _nextWarning = now + 45f;
                    Plugin.Logger.LogInfo("[CastMetrics] Waiting for game's combat.log; DPS is unavailable until it exists.");
                }
                return;
            }

            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long length = file.Length;
            DateTime created = File.GetCreationTimeUtc(_path);

            if (!_filePresent || length < _position || created != _fileCreationUtc)
            {
                bool reset = _filePresent;
                _filePresent = true;
                _fileCreationUtc = created;
                _position = 0;
                Pending.Clear();
                ClearEncounter();
                if (!_manualName) _playerName = "";

                // Determine the local character from the FIRST class-switch
                // entry in this session (unlike later entries, which include
                // nearby players). Existing historical damage is not replayed.
                int headerCount = (int)Math.Min(length, 16384L);
                if (headerCount > 0)
                {
                    var header = new byte[headerCount];
                    file.ReadExactly(header, 0, header.Length);
                    string text = Encoding.UTF8.GetString(header);
                    foreach (string line in text.Split('\n'))
                    {
                        ObserveClassSwitch(line.TrimEnd('\r'));
                        if (_playerName.Length > 0) break;
                    }
                }

                _position = length;
                if (reset)
                    Plugin.Logger.LogInfo("[CastMetrics] Combat log restarted/truncated; previous encounter cleared.");
                if (_playerName.Length == 0)
                    Plugin.Logger.LogInfo("[CastMetrics] Waiting for your first class-switch entry to identify local DPS.");
                return;
            }

            if (length <= _position) return;
            file.Position = _position;
            int allowance = MaxBytesPerPoll;
            while (_position < length && allowance > 0)
            {
                int wanted = (int)Math.Min(Math.Min(length - _position, Buffer.Length), allowance);
                int read = file.Read(Buffer, 0, wanted);
                if (read <= 0) break;
                _position += read;
                allowance -= read;

                for (int i = 0; i < read; i++)
                {
                    byte value = Buffer[i];
                    if (value == (byte)'\n')
                    {
                        string line = Encoding.UTF8.GetString(Pending.ToArray()).TrimEnd('\r');
                        Pending.Clear();
                        ProcessLine(line, now);
                    }
                    else if (Pending.Count < MaxLineBytes)
                    {
                        Pending.Add(value);
                    }
                    else
                    {
                        // Drop unexpectedly huge/malformed lines instead of
                        // growing memory without bound.
                        Pending.Clear();
                    }
                }
            }
        }
        catch (IOException exception)
        {
            WarnReadFailure(now, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            WarnReadFailure(now, exception.Message);
        }
        catch (ArgumentException exception)
        {
            WarnReadFailure(now, exception.Message);
        }
    }

    private static void WarnReadFailure(float now, string message)
    {
        if (now >= _nextWarning)
        {
            _nextWarning = now + 45f;
            Plugin.Logger.LogWarning("[CastMetrics] Combat log temporarily unreadable: " + message);
        }
    }

    private static void ObserveClassSwitch(string line)
    {
        if (_manualName || _playerName.Length != 0) return;
        Match match = ClassSwitch.Match(line);
        if (!match.Success) return;

        string candidate = match.Groups[1].Value.Trim();
        if (candidate.Length == 0 || candidate.Length > 64) return;
        _playerName = candidate;
        Plugin.Logger.LogInfo("[CastMetrics] Local DPS character automatically identified as '" +
                              _playerName + "' from the first class switch in combat.log.");
    }

    private static void ProcessLine(string line, float now)
    {
        if (line.Length == 0) return;
        if (_observedLines < int.MaxValue) _observedLines++;
        ObserveClassSwitch(line);

        if (line.EndsWith("[Combat] You are no longer in combat.", StringComparison.Ordinal))
        {
            _inCombat = false;
            ClearEncounter();
            return;
        }
        if (line.EndsWith("[Combat] You are now in combat.", StringComparison.Ordinal))
        {
            _inCombat = true;
            return;
        }
        if (_playerName.Length == 0) return;

        Match match = DamageLine.Match(line);
        if (!match.Success) return;

        string target = match.Groups["target"].Value;
        string source = match.Groups["source"].Value;
        if (!string.Equals(source, _playerName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, _playerName, StringComparison.OrdinalIgnoreCase))
            return;

        if (!int.TryParse(match.Groups["value"].Value, NumberStyles.Integer,
                          CultureInfo.InvariantCulture, out int amount) ||
            amount <= 0 || amount > 100000000)
            return;

        string time = match.Groups["time"].Value;
        if (!TimeSpan.TryParseExact(time, @"hh\:mm\:ss\:fff",
                                    CultureInfo.InvariantCulture, out TimeSpan parsed))
            return;
        double seconds = parsed.TotalSeconds;

        if (!_encounterHasDamage || now - _lastHitLocalTime > MaxEncounterIdle)
        {
            ClearEncounter();
            _encounterHasDamage = true;
            _firstHitLogSeconds = seconds;
            _encounters++;
        }

        _lastHitLogSeconds = seconds;
        _lastHitLocalTime = now;
        _damageTotal += amount;
        _damageEvents++;
        _inCombat = true;
        if (_damageEvents == 1)
            Plugin.Logger.LogInfo("[CastMetrics] First confirmed outgoing damage event: " +
                amount + " to " + target + " from " + _playerName +
                ". DPS is now driven by the combat log, not monster-HP guesses.");
    }

    internal static float Dps(float now)
    {
        if (!IsReady || !_encounterHasDamage || !_inCombat) return 0f;
        if (now - _lastHitLocalTime > MaxEncounterIdle)
        {
            ClearEncounter();
            return 0f;
        }

        // Event timestamps preserve accurate hit spacing even when multiple
        // log lines arrive in a single file poll. Realtime advances the clock
        // between hits. Cap the initial divisor at 1s to avoid huge spikes.
        double logElapsed = _lastHitLogSeconds - _firstHitLogSeconds;
        if (logElapsed < 0) logElapsed += 24d * 60d * 60d;
        double elapsed = Math.Max(1d, logElapsed + Math.Max(0f, now - _lastHitLocalTime));
        return (float)(_damageTotal / elapsed);
    }

    private static void ClearEncounter()
    {
        _encounterHasDamage = false;
        _firstHitLogSeconds = 0d;
        _lastHitLogSeconds = 0d;
        _lastHitLocalTime = 0f;
        _damageTotal = 0L;
        _damageEvents = 0;
    }
}
