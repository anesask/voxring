namespace Loupedeck.VoxRingPlugin.Helpers;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
/// Runaway-invalidation detector for meter actions.
///
/// Meters route their AdjustmentValueChanged() / ActionImageChanged() calls through <see cref="Fire"/>.
/// If the cumulative invalidation rate across all meters sustains above <see cref="WarnThresholdPerSecond"/>
/// for longer than <see cref="Window"/>, we emit one WARN line to the plugin log (throttled by
/// <see cref="WarnCooldown"/>) naming the top offender. The invalidation itself is never blocked — the
/// watchdog is diagnostic, not prescriptive.
///
/// Why: previously, four meters each ran unconditional timers totalling ~21 Hz of forced redraws, which
/// saturated the Options+ IPC and made the UI unresponsive. This helper catches that class of regression
/// in the log instead of in a frozen UI.
/// </summary>
internal static class MeterWatchdog
{
    private const int WarnThresholdPerSecond = 30;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WarnCooldown = TimeSpan.FromSeconds(5);

    private static readonly ConcurrentQueue<Entry> Recent = new();
    private static readonly object WarnLock = new();
    private static DateTime _lastWarnUtc = DateTime.MinValue;

    private readonly struct Entry
    {
        public Entry(DateTime time, string meter) { this.Time = time; this.Meter = meter; }
        public DateTime Time { get; }
        public string Meter { get; }
    }

    public static void Fire(string meterName, Action invalidate)
    {
        if (invalidate == null) return;

        var now = DateTime.UtcNow;
        Recent.Enqueue(new Entry(now, meterName ?? "unknown"));

        // Drain entries outside the rolling window.
        while (Recent.TryPeek(out var head) && now - head.Time > Window)
        {
            Recent.TryDequeue(out _);
        }

        var count = Recent.Count;
        if (count > WarnThresholdPerSecond)
        {
            MaybeWarn(now, count);
        }

        try
        {
            invalidate();
        }
        catch (Exception ex)
        {
            // A thrown invalidation would otherwise kill the timer thread silently.
            PluginLog.Warning(ex, $"[MeterWatchdog] invalidate threw for {meterName}");
        }
    }

    private static void MaybeWarn(DateTime now, int count)
    {
        lock (WarnLock)
        {
            if (now - _lastWarnUtc < WarnCooldown) return;
            _lastWarnUtc = now;
        }

        // Identify the loudest contributor in the current window for the log message.
        var tally = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in Recent)
        {
            tally.TryGetValue(entry.Meter, out var n);
            tally[entry.Meter] = n + 1;
        }

        string topMeter = "?";
        int topCount = 0;
        foreach (var kv in tally)
        {
            if (kv.Value > topCount) { topMeter = kv.Key; topCount = kv.Value; }
        }

        PluginLog.Warning(
            $"[MeterWatchdog] {count} meter invalidations in last {Window.TotalSeconds:0}s " +
            $"(threshold {WarnThresholdPerSecond}/s). Top offender: {topMeter} ({topCount}). " +
            $"Check OnLoad timer guards — unconditional AdjustmentValueChanged() floods the SDK IPC.");
    }
}
