using System;
using System.Collections.Generic;
using System.IO;

namespace OmenCore.Services;

public enum OsdFpsDisplayState
{
    Unavailable,
    Critical,
    Warning,
    Good
}

public sealed record OsdFpsDisplaySnapshot(
    string Label,
    string Display,
    string Detail,
    string FrametimeDisplay,
    double Fps,
    double FrametimeMs,
    OsdFpsDisplayState State);

public static class OsdFpsDisplayFormatter
{
    public static OsdFpsDisplaySnapshot FromRtss(
        float instantFps,
        float averageFps,
        float minFps,
        float onePercentLowFps,
        float frametimeMs,
        string? processName)
    {
        var displayFps = IsUsable(instantFps)
            ? instantFps
            : IsUsable(averageFps)
                ? averageFps
                : 0;

        if (displayFps <= 0)
        {
            return Unavailable("RTSS waiting for frames");
        }

        var effectiveFrametime = IsUsable(frametimeMs)
            ? frametimeMs
            : 1000.0 / displayFps;

        return new OsdFpsDisplaySnapshot(
            "FPS",
            $"{displayFps:F0}",
            BuildDetail(displayFps, averageFps, minFps, onePercentLowFps, processName),
            $"{effectiveFrametime:F1}ms",
            displayFps,
            effectiveFrametime,
            Classify(displayFps, onePercentLowFps));
    }

    public static OsdFpsDisplaySnapshot Unavailable(string? detail)
    {
        return new OsdFpsDisplaySnapshot(
            "FPS",
            "N/A",
            string.IsNullOrWhiteSpace(detail) ? "FPS unavailable" : detail.Trim(),
            "--",
            0,
            0,
            OsdFpsDisplayState.Unavailable);
    }

    private static string BuildDetail(
        double displayFps,
        float averageFps,
        float minFps,
        float onePercentLowFps,
        string? processName)
    {
        var parts = new List<string>(3);

        if (IsUsable(averageFps) && Math.Abs(averageFps - displayFps) >= 0.5)
        {
            parts.Add($"avg {averageFps:F0}");
        }

        if (IsUsable(onePercentLowFps))
        {
            parts.Add($"1% {onePercentLowFps:F0}");
        }
        else if (IsUsable(minFps) && minFps < displayFps)
        {
            parts.Add($"min {minFps:F0}");
        }

        var process = NormalizeProcessName(processName);
        if (!string.IsNullOrWhiteSpace(process))
        {
            parts.Add(process);
        }

        return string.Join(" | ", parts);
    }

    private static OsdFpsDisplayState Classify(double fps, float onePercentLowFps)
    {
        var pacingFloor = IsUsable(onePercentLowFps) ? onePercentLowFps : fps;
        var qualityFps = Math.Min(fps, pacingFloor);

        if (qualityFps < 30)
        {
            return OsdFpsDisplayState.Critical;
        }

        if (qualityFps < 55)
        {
            return OsdFpsDisplayState.Warning;
        }

        return OsdFpsDisplayState.Good;
    }

    private static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var trimmed = processName.Split('\0')[0].Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var fileName = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(fileName) ? trimmed : fileName;
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
    }

    private static bool IsUsable(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
    }
}
